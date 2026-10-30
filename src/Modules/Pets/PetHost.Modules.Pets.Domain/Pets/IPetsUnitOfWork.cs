namespace PetHost.Modules.Pets.Domain.Pets;

/// <summary>Confirma a transação do módulo Pets.</summary>
public interface IPetsUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
