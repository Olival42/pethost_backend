namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>Confirma a transação do módulo Listings.</summary>
public interface IListingsUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
