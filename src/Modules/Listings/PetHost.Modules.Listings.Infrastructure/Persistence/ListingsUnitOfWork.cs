using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Infrastructure.Persistence;

/// <summary>Confirma a transação do módulo Listings.</summary>
internal sealed class ListingsUnitOfWork(ListingsDbContext dbContext) : IListingsUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
