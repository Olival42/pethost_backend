using PetHost.Modules.Owners.Domain.Owners;

namespace PetHost.Modules.Owners.Infrastructure.Persistence;

/// <summary>Confirma a transação do módulo Owners.</summary>
internal sealed class OwnersUnitOfWork(OwnersDbContext dbContext) : IOwnersUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
