using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Shared.Contracts.Payments;

namespace PetHost.Modules.Hosts.Infrastructure.Payments;

/// <summary>Adaptador da porta <see cref="IHostEarnings"/> sobre o contrato público <see cref="IPaymentDirectory"/>.</summary>
internal sealed class HostEarnings(IPaymentDirectory paymentDirectory) : IHostEarnings
{
    public Task<HostEarningsSummary> GetAsync(Guid hostId, DateOnly month, CancellationToken cancellationToken) =>
        paymentDirectory.GetHostEarningsAsync(hostId, month, cancellationToken);
}
