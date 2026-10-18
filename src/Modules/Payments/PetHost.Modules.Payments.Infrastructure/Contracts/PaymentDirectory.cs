using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Contracts.Payments;

namespace PetHost.Modules.Payments.Infrastructure.Contracts;

/// <summary>
/// Adaptador do contrato <see cref="IPaymentDirectory"/> (§5): o Bookings pergunta se o tutor está
/// devendo; o Hosts, os ganhos do anfitrião para o painel.
/// </summary>
internal sealed class PaymentDirectory(IPaymentRepository paymentRepository) : IPaymentDirectory
{
    public Task<bool> HasOverdueInstallmentAsync(Guid ownerId, CancellationToken cancellationToken) =>
        paymentRepository.HasOverdueInstallmentAsync(ownerId, cancellationToken);

    public Task<bool> HasPaidAnyInstallmentAsync(Guid ownerId, CancellationToken cancellationToken) =>
        paymentRepository.HasPaidAnyInstallmentAsync(ownerId, cancellationToken);

    /// <summary>A conta fica no domínio (<see cref="HostEarningsReport"/>); aqui só a leitura e o formato do contrato.</summary>
    public async Task<HostEarningsSummary> GetHostEarningsAsync(Guid hostId, DateOnly month, CancellationToken cancellationToken)
    {
        var payments = await paymentRepository.ListPaidByHostAsync(hostId, cancellationToken).ConfigureAwait(false);
        var report = HostEarningsReport.For(payments, month);

        return new HostEarningsSummary(
            report.Transferred,
            report.Reversed,
            report.PreviousMonthTransferred,
            report.YearToDateTransferred,
            new HostEarningsBreakdown(report.PaidByOwners, report.LateFees, report.Refunded, report.PlatformFee),
            new HostHeldPayouts(report.AwaitingCheckOut, report.Disputed, report.Blocked),
            new HostExpectedPayouts(report.Expected, report.ExpectedInstallments));
    }
}
