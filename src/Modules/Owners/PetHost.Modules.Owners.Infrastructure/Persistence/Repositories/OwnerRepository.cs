using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Owners.Domain.Owners;

namespace PetHost.Modules.Owners.Infrastructure.Persistence.Repositories;

/// <summary>Implementação de <see cref="IOwnerRepository"/> sobre o EF Core.</summary>
internal sealed class OwnerRepository(OwnersDbContext dbContext) : IOwnerRepository
{
    public Task<bool> ExistsByCpfAsync(Cpf cpf, CancellationToken cancellationToken) =>
        dbContext.Owners
            .AsNoTracking()
            .AnyAsync(o => o.Cpf == cpf, cancellationToken);

    public Task<Owner?> GetByIdAsync(OwnerId id, CancellationToken cancellationToken) =>
        dbContext.Owners.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    public Task<Owner?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Owners.FirstOrDefaultAsync(o => o.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<Owner>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Owners
            .AsNoTracking()
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(Owner owner) => dbContext.Owners.Add(owner);
}
