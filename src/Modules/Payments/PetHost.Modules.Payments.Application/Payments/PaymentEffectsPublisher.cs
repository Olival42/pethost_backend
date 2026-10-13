using System.Globalization;
using PetHost.Modules.Payments.Application.Abstractions;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Contracts.Audit;

namespace PetHost.Modules.Payments.Application.Payments;

/// <summary>
/// Publica os efeitos de uma operação depois que ela gravou: cada fato vai para a trilha
/// (<c>payment.*</c>, sem dado pessoal) e vira o aviso de quem precisa saber — o tutor (parcela
/// paga, lembrete, atraso, reembolso), o anfitrião (repasse, estorno) ou o admin (contestação,
/// reembolso que falhou, estorno, atraso).
/// </summary>
public sealed class PaymentEffectsPublisher(
    IPaymentParties parties,
    IPaymentNotifier notifier,
    IAuditTrail auditTrail)
{
    public async Task PublishAsync(Payment payment, PaymentEffects effects, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(effects);

        if (effects.Items.Count == 0)
            return;

        PartyContact? owner = null;
        PartyContact? host = null;

        foreach (var effect in effects.Items)
        {
            await RecordAsync(payment, effect, cancellationToken).ConfigureAwait(false);

            var dueAt = payment.FindInstallment(effect.InstallmentNumber)?.DueAt;

            switch (effect.Kind)
            {
                case PaymentEffectKind.InstallmentPaid:
                case PaymentEffectKind.InstallmentReminder:
                case PaymentEffectKind.RefundCreated:
                    owner ??= await parties.GetOwnerContactAsync(payment.OwnerId, cancellationToken).ConfigureAwait(false);
                    await NotifyAsync(owner, NoticeKind(effect.Kind), payment, effect, dueAt, cancellationToken).ConfigureAwait(false);
                    break;

                case PaymentEffectKind.InstallmentOverdue:
                    owner ??= await parties.GetOwnerContactAsync(payment.OwnerId, cancellationToken).ConfigureAwait(false);
                    await NotifyAsync(owner, PaymentNoticeKind.InstallmentOverdue, payment, effect, dueAt, cancellationToken).ConfigureAwait(false);
                    await AlertAsync("installment_overdue", payment, effect, cancellationToken).ConfigureAwait(false);
                    break;

                case PaymentEffectKind.PayoutTransferred:
                    host ??= await parties.GetHostContactAsync(payment.HostId, cancellationToken).ConfigureAwait(false);
                    await NotifyAsync(host, PaymentNoticeKind.PayoutTransferred, payment, effect, dueAt, cancellationToken).ConfigureAwait(false);
                    break;

                case PaymentEffectKind.PayoutReversed:
                    host ??= await parties.GetHostContactAsync(payment.HostId, cancellationToken).ConfigureAwait(false);
                    await NotifyAsync(host, PaymentNoticeKind.PayoutReversed, payment, effect, dueAt, cancellationToken).ConfigureAwait(false);
                    await AlertAsync("payout_reversed", payment, effect, cancellationToken).ConfigureAwait(false);
                    break;

                case PaymentEffectKind.RefundFailed:
                    await AlertAsync("refund_failed", payment, effect, cancellationToken).ConfigureAwait(false);
                    break;

                case PaymentEffectKind.PayoutReversalFailed:
                    await AlertAsync("payout_reversal_failed", payment, effect, cancellationToken).ConfigureAwait(false);
                    break;

                case PaymentEffectKind.DisputeOpened:
                    await AlertAsync("dispute_opened", payment, effect, cancellationToken).ConfigureAwait(false);
                    break;

                case PaymentEffectKind.DisputeClosed:
                    await AlertAsync($"dispute_{effect.Detail}", payment, effect, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    private Task NotifyAsync(
        PartyContact? to,
        PaymentNoticeKind kind,
        Payment payment,
        PaymentEffect effect,
        DateTimeOffset? dueAt,
        CancellationToken cancellationToken) =>
        to is null
            ? Task.CompletedTask
            : notifier.NotifyAsync(
                new PaymentNotice(kind, to.Email, to.Name, payment.BookingId, effect.InstallmentNumber, payment.InstallmentCount, effect.AmountCents, dueAt),
                cancellationToken);

    private Task AlertAsync(string issue, Payment payment, PaymentEffect effect, CancellationToken cancellationToken) =>
        notifier.AlertAdminAsync(new PaymentAlert(issue, payment.Id.Value, payment.BookingId, effect.InstallmentNumber, effect.AmountCents), cancellationToken);

    /// <summary>Fatos que mudam o dinheiro de lugar vão para a trilha; lembrete e falha de estorno, não.</summary>
    private Task RecordAsync(Payment payment, PaymentEffect effect, CancellationToken cancellationToken)
    {
        var action = effect.Kind switch
        {
            PaymentEffectKind.PlanChosen => AuditActions.PaymentPlanChosen,
            PaymentEffectKind.InstallmentPaid => AuditActions.PaymentInstallmentPaid,
            PaymentEffectKind.InstallmentOverdue => AuditActions.PaymentInstallmentOverdue,
            PaymentEffectKind.RefundCreated => AuditActions.PaymentRefundCreated,
            PaymentEffectKind.RefundFailed => AuditActions.PaymentRefundFailed,
            PaymentEffectKind.PayoutTransferred => AuditActions.PaymentPayoutTransferred,
            PaymentEffectKind.PayoutReversed => AuditActions.PaymentPayoutReversed,
            PaymentEffectKind.DisputeOpened => AuditActions.PaymentDisputeOpened,
            PaymentEffectKind.DisputeClosed => AuditActions.PaymentDisputeClosed,
            _ => null,
        };

        if (action is null)
            return Task.CompletedTask;

        var details = new Dictionary<string, string?>
        {
            ["bookingId"] = payment.BookingId.ToString(),
            ["installment"] = effect.InstallmentNumber.ToString(CultureInfo.InvariantCulture),
            ["amountCents"] = effect.AmountCents.ToString(CultureInfo.InvariantCulture),
        };

        if (effect.Detail is not null)
            details["detail"] = effect.Detail;

        if (effect.Kind == PaymentEffectKind.PlanChosen)
            details["installmentCount"] = payment.InstallmentCount.ToString(CultureInfo.InvariantCulture);

        return auditTrail.RecordAsync(new AuditRecord(action, AuditTargets.Payment, payment.Id.Value, Details: details), cancellationToken);
    }

    private static PaymentNoticeKind NoticeKind(PaymentEffectKind kind) => kind switch
    {
        PaymentEffectKind.InstallmentPaid => PaymentNoticeKind.InstallmentPaid,
        PaymentEffectKind.InstallmentReminder => PaymentNoticeKind.InstallmentReminder,
        PaymentEffectKind.RefundCreated => PaymentNoticeKind.RefundCreated,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a notice to the owner."),
    };
}
