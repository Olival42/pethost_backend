using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>Confirma a transação do módulo Payments.</summary>
public interface IPaymentsUnitOfWork
{
    /// <summary>
    /// Grava tudo numa transação. Duas mudanças no mesmo pagamento ao mesmo tempo (o tutor abre o
    /// pagamento enquanto chega o webhook) voltam como <c>409 PAYMENT_CONFLICT</c>, não como exceção (§15).
    /// </summary>
    Task<Result> SaveChangesAsync(CancellationToken cancellationToken);
}
