using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Shared.Contracts.Payments;

namespace PetHost.Modules.Owners.Infrastructure.Payments;

/// <summary>Adaptador da porta <see cref="IOwnerPayments"/> sobre o contrato público <see cref="IPaymentDirectory"/>.</summary>
internal sealed class OwnerPayments(IPaymentDirectory paymentDirectory) : IOwnerPayments
{
    public Task<bool> HasPaidAsync(Guid ownerId, CancellationToken cancellationToken) =>
        paymentDirectory.HasPaidAnyInstallmentAsync(ownerId, cancellationToken);
}
