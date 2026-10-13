using PetHost.Modules.Payments.Application.Abstractions;
using PetHost.Modules.Payments.Domain.Errors;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Contracts.Bookings;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Payments.Application.Payments;

/// <summary>
/// Leva o pagamento ao estado certo dada a reserva e o Stripe agora. É o mesmo caminho para o
/// webhook, para o aviso do Bookings (cancelou, concluiu, expirou), para a releitura pedida pelo
/// app e para a manutenção periódica — por isso perder um aviso só atrasa, nunca desencontra.
/// </summary>
/// <remarks>
/// Ordem: (1) 1ª parcela paga confirma a reserva aceita; (2) parcela vencida cancela a reserva
/// (antes do check-in) ou entra em atraso (depois); (3) lembretes; (4) os pedidos ao Stripe que o
/// agregado decide (<see cref="Payment.Reconcile"/>) — cancelar cobranças, reembolsar, repassar,
/// estornar —, em rodadas até não sobrar nada (o repasse do que ficou retido sai depois do reembolso).
/// Um pedido que falha no Stripe fica para a próxima vez: o resto segue e o que deu certo é gravado.
/// </remarks>
public sealed class PaymentReconciler(
    IStripePayments stripe,
    IPaymentBookings bookings,
    IPaymentParties parties,
    TimeProvider timeProvider)
{
    /// <summary>Rodadas de pedidos ao Stripe por reconciliação (reembolso → repasse precisa de duas).</summary>
    private const int MaxRounds = 4;

    /// <summary>
    /// As parcelas que valem uma releitura no Stripe. Na manutenção, só o que pode ter mudado sem
    /// aviso: cobrança em processamento, reembolso a caminho, contestação aberta. Pedida pelo app
    /// (<paramref name="everything"/>), toda parcela com cobrança que ainda não chegou ao fim.
    /// </summary>
    public static IReadOnlyList<Installment> NeedingRefresh(Payment payment, bool everything)
    {
        ArgumentNullException.ThrowIfNull(payment);

        return [.. payment.Installments.Where(i => i.StripePaymentIntentId is not null && (everything
            ? !i.IsSettled
            : i.Status == InstallmentStatus.Processing
                || i.RefundStatus == RefundStatus.Pending
                || i.DisputeStatus == DisputeStatus.Open))];
    }

    /// <summary>Relê no Stripe a cobrança da parcela e aplica ao pagamento.</summary>
    public async Task<Result> RefreshAsync(Payment payment, Installment installment, PaymentEffects effects, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(installment);
        ArgumentNullException.ThrowIfNull(effects);

        if (installment.StripePaymentIntentId is not { } intent)
            return Result.Success();

        var snapshot = await stripe.GetChargeAsync(intent, cancellationToken).ConfigureAwait(false);
        if (snapshot.IsFailure)
            return Result.Failure(snapshot.Errors!);

        var change = payment.ApplyCharge(installment.Number, snapshot.Value!, timeProvider.GetUtcNow());
        effects.Add(change, installment);

        return Result.Success();
    }

    /// <summary>
    /// Reconcilia com a reserva atual. Falha só quando a reserva mudou ao mesmo tempo
    /// (<c>409 PAYMENT_CONFLICT</c>, tente de novo); pedido recusado ou Stripe fora do ar fica para depois.
    /// </summary>
    public async Task<Result> ReconcileAsync(Payment payment, PaymentEffects effects, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(effects);

        var now = timeProvider.GetUtcNow();
        var booking = await bookings.GetAsync(payment.BookingId, cancellationToken).ConfigureAwait(false);

        if (booking?.Stage == BookingStage.AwaitingPayment && payment.Installments.Any(i => i.IsPaid))
        {
            var confirmed = await bookings.ConfirmPaidAsync(payment.BookingId, cancellationToken).ConfigureAwait(false);
            if (confirmed == BookingPaymentOutcome.Conflict)
                return Result.Failure(PaymentsErrors.Conflict);

            booking = await bookings.GetAsync(payment.BookingId, cancellationToken).ConfigureAwait(false);
        }

        var overdue = payment.CheckOverdue(StageOf(booking), now);
        if (overdue.MissedBeforeCheckIn)
        {
            var cancelled = await bookings.CancelUnpaidAsync(payment.BookingId, cancellationToken).ConfigureAwait(false);
            if (cancelled == BookingPaymentOutcome.Conflict)
                return Result.Failure(PaymentsErrors.Conflict);

            booking = await bookings.GetAsync(payment.BookingId, cancellationToken).ConfigureAwait(false);
        }

        foreach (var number in overdue.BecameOverdue)
            effects.Add(PaymentEffectKind.InstallmentOverdue, number, payment.FindInstallment(number)!.AmountCents);

        foreach (var installment in payment.TakeReminders(now))
            effects.Add(PaymentEffectKind.InstallmentReminder, installment.Number, installment.AmountCents);

        for (var round = 0; round < MaxRounds; round++)
        {
            var actions = payment.Reconcile(StageOf(booking), booking?.RefundPercentage, timeProvider.GetUtcNow());
            if (actions.Count == 0)
                break;

            var progressed = false;
            foreach (var action in actions)
                progressed |= await ExecuteAsync(payment, action, effects, cancellationToken).ConfigureAwait(false);

            if (!progressed)
                break;
        }

        return Result.Success();
    }

    /// <summary>Reserva que sumiu conta como fechada: o que foi pago volta inteiro.</summary>
    private static BookingStage StageOf(PaymentBooking? booking) => booking?.Stage ?? BookingStage.Closed;

    /// <summary>Executa um pedido no Stripe e registra o resultado. Devolve se algo mudou.</summary>
    private async Task<bool> ExecuteAsync(Payment payment, PaymentAction action, PaymentEffects effects, CancellationToken cancellationToken)
    {
        var number = action.InstallmentNumber;
        var installment = payment.FindInstallment(number)!;

        switch (action.Kind)
        {
            case PaymentActionKind.CancelIntent:
            {
                var cancelled = await stripe.CancelIntentAsync(action.StripeObjectId, cancellationToken).ConfigureAwait(false);
                if (cancelled.IsSuccess)
                {
                    payment.RecordIntentCancelled(number, timeProvider.GetUtcNow());
                    return true;
                }

                // Recusado: talvez o tutor tenha pagado nesse meio-tempo. Relê; se pagou, a próxima rodada devolve.
                if (cancelled.FirstError?.Code != PaymentsErrors.StripeRequestRejected.Code)
                    return false;

                var refreshed = await RefreshAsync(payment, installment, effects, cancellationToken).ConfigureAwait(false);
                return refreshed.IsSuccess && (installment.IsPaid || !installment.IntentOpen);
            }

            case PaymentActionKind.Refund:
            {
                var refund = await stripe
                    .RefundAsync(new RefundDraft(installment.Id, payment.Id.Value, number, action.StripeObjectId, action.AmountCents), cancellationToken)
                    .ConfigureAwait(false);

                if (refund.IsFailure)
                    return false;

                var value = refund.Value!;
                payment.RecordRefund(number, value.Id, value.AmountCents, value.Status, timeProvider.GetUtcNow());
                effects.Add(PaymentEffectKind.RefundCreated, number, value.AmountCents);

                if (value.Status == RefundStatus.Failed)
                    effects.Add(PaymentEffectKind.RefundFailed, number, value.AmountCents);

                return true;
            }

            case PaymentActionKind.Transfer:
            {
                var account = await parties.GetHostPayoutAccountAsync(payment.HostId, cancellationToken).ConfigureAwait(false);
                if (account is null)
                {
                    payment.RecordTransferFailed(number, NoConnectedAccount, timeProvider.GetUtcNow());
                    return false;
                }

                var transfer = await stripe
                    .TransferAsync(new TransferDraft(installment.Id, payment.Id.Value, payment.BookingId, number, action.StripeObjectId, account, action.AmountCents), cancellationToken)
                    .ConfigureAwait(false);

                if (transfer.IsFailure)
                {
                    payment.RecordTransferFailed(number, transfer.FirstError!.Code, timeProvider.GetUtcNow());
                    return false;
                }

                payment.RecordTransfer(number, transfer.Value!, action.AmountCents, action.PlatformFeeCents, timeProvider.GetUtcNow());
                effects.Add(PaymentEffectKind.PayoutTransferred, number, action.AmountCents);
                return true;
            }

            case PaymentActionKind.ReverseTransfer:
            {
                var reversal = await stripe
                    .ReverseTransferAsync(new ReversalDraft(installment.Id, payment.Id.Value, number, action.StripeObjectId, action.AmountCents), cancellationToken)
                    .ConfigureAwait(false);

                if (reversal.IsFailure)
                {
                    // Avisa o admin só na primeira falha; a manutenção continua tentando.
                    if (installment.PayoutFailureCode is null)
                        effects.Add(PaymentEffectKind.PayoutReversalFailed, number, action.AmountCents);

                    payment.RecordTransferFailed(number, reversal.FirstError!.Code, timeProvider.GetUtcNow());
                    return false;
                }

                payment.RecordReversal(number, reversal.Value!, action.AmountCents, timeProvider.GetUtcNow());
                effects.Add(PaymentEffectKind.PayoutReversed, number, action.AmountCents);
                return true;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(action), action.Kind, "Unknown payment action.");
        }
    }

    /// <summary>Código gravado quando o anfitrião não tem conta conectada para receber.</summary>
    public const string NoConnectedAccount = "NO_CONNECTED_ACCOUNT";
}
