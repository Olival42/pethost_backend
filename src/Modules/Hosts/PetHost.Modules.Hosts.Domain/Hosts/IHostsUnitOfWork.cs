using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Domain.Hosts;

/// <summary>Confirma a transação do módulo Hosts.</summary>
public interface IHostsUnitOfWork
{
    /// <summary>
    /// Grava tudo numa transação. Duas pessoas mexendo na mesma solicitação (dois admins
    /// decidindo juntos) ou dois cadastros com o mesmo CPF ao mesmo tempo voltam como erro
    /// (<c>409</c>), não como exceção (§15).
    /// </summary>
    Task<Result> SaveChangesAsync(CancellationToken cancellationToken);
}
