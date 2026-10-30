using PetHost.Modules.Pets.Domain.Pets;

namespace PetHost.Modules.Pets.Infrastructure.Persistence;

/// <summary>Confirma a transação do módulo Pets.</summary>
internal sealed class PetsUnitOfWork(PetsDbContext dbContext) : IPetsUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
