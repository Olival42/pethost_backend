using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Contracts.Payments;

namespace PetHost.Modules.Payments.Infrastructure.Contracts;

/// <summary>Adaptador do contrato <see cref="IPaymentDirectory"/> (§5): o Bookings pergunta se o tutor está devendo.</summary>
internal sealed class PaymentDirectory(IPaymentRepository paymentRepository) : IPaymentDirectory
{
    public Task<bool> HasOverdueInstallmentAsync(Guid ownerId, CancellationToken cancellationToken) =>
        paymentRepository.HasOverdueInstallmentAsync(ownerId, cancellationToken);
}
