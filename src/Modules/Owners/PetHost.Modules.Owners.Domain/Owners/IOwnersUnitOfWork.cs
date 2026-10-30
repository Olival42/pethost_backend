namespace PetHost.Modules.Owners.Domain.Owners;

/// <summary>Confirma a transação do módulo Owners.</summary>
public interface IOwnersUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
