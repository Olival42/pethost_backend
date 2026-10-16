using FluentValidation;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Payments.Application.Payments.RunPaymentsMaintenance;

/// <summary>
/// Uma rodada da manutenção em segundo plano: revisita os pagamentos que ainda têm o que fazer —
/// relê no Stripe o que pode ter mudado sem webhook (cobrança em processamento, reembolso a
/// caminho, contestação aberta), cancela a reserva com parcela vencida antes do check-in, marca
/// atraso depois dele, manda lembretes e refaz o reembolso ou o repasse que falhou.
/// </summary>
public sealed record RunPaymentsMaintenanceCommand : ICommand<RunPaymentsMaintenanceResponse>
{
    /// <summary>Quantos pagamentos uma rodada revisita, no máximo; o resto fica para a próxima.</summary>
    public const int BatchSize = 100;
}

/// <summary>Quantos pagamentos foram revisitados e em quantos algo falhou (fica para a próxima rodada).</summary>
public sealed record RunPaymentsMaintenanceResponse(int Reviewed, int Failed);

/// <summary>Sem entrada: nada a validar (§10 pede validador em todo command).</summary>
public sealed class RunPaymentsMaintenanceCommandValidator : AbstractValidator<RunPaymentsMaintenanceCommand>;

/// <summary>
/// Um pagamento por vez, cada um gravado sozinho: Stripe fora do ar ou conflito num não impede os
/// outros. Os revisitados vão para o fim da fila (<see cref="Payment.UpdatedAt"/>).
/// </summary>
public sealed class RunPaymentsMaintenanceCommandHandler(
    IPaymentRepository paymentRepository,
    PaymentReconciler reconciler,
    PaymentCommitter committer) : ICommandHandler<RunPaymentsMaintenanceCommand, RunPaymentsMaintenanceResponse>
{
    public async Task<Result<RunPaymentsMaintenanceResponse>> HandleAsync(
        RunPaymentsMaintenanceCommand command,
        CancellationToken cancellationToken)
    {
        var bookingIds = await paymentRepository
            .ListUnsettledBookingIdsAsync(RunPaymentsMaintenanceCommand.BatchSize, cancellationToken)
            .ConfigureAwait(false);

        var failed = 0;

        foreach (var bookingId in bookingIds)
        {
            var payment = await paymentRepository.GetByBookingIdAsync(bookingId, cancellationToken).ConfigureAwait(false);
            if (payment is null)
                continue;

            if (!await ReviewAsync(payment, cancellationToken).ConfigureAwait(false))
                failed++;
        }

        return Result<RunPaymentsMaintenanceResponse>.Success(new RunPaymentsMaintenanceResponse(bookingIds.Count, failed));
    }

    private async Task<bool> ReviewAsync(Payment payment, CancellationToken cancellationToken)
    {
        var effects = new PaymentEffects();
        var ok = true;

        foreach (var installment in PaymentReconciler.NeedingRefresh(payment, everything: false))
            ok &= (await reconciler.RefreshAsync(payment, installment, effects, cancellationToken).ConfigureAwait(false)).IsSuccess;

        var reconciled = await reconciler.ReconcileAsync(payment, effects, cancellationToken).ConfigureAwait(false);
        var committed = await committer.CommitAsync(payment, effects, cancellationToken).ConfigureAwait(false);

        return ok && reconciled.IsSuccess && committed.IsSuccess;
    }
}
