using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Modules.Owners.Infrastructure.Persistence;
using PetHost.Shared.Contracts.Owners;

namespace PetHost.Modules.Owners.Infrastructure.Accounts;

/// <summary>
/// Adaptador do contrato <see cref="IOwnerDirectory"/> (§5): a porta pela qual outro módulo
/// acha o tutor. Leitura direta da tabela, sem CPF.
/// </summary>
internal sealed class OwnerDirectory(OwnersDbContext dbContext) : IOwnerDirectory
{
    public async Task<OwnerReference?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var owner = await dbContext.Owners
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        return owner is null ? null : ToReference(owner);
    }

    public async Task<IReadOnlyList<OwnerReference>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ownerIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownerIds);

        if (ownerIds.Count == 0)
            return [];

        var ids = ownerIds.Select(id => new OwnerId(id)).ToList();

        var owners = await dbContext.Owners
            .AsNoTracking()
            .Where(o => ids.Contains(o.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. owners.Select(ToReference)];
    }

    private static OwnerReference ToReference(Owner owner) => new(owner.Id.Value, owner.UserId, owner.IsActive);
}
