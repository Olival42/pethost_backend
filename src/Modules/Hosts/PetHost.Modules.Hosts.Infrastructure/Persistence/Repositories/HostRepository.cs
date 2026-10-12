using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Hosts.Domain.Hosts;

namespace PetHost.Modules.Hosts.Infrastructure.Persistence.Repositories;

/// <summary>Implementação de <see cref="IHostRepository"/> sobre o EF Core.</summary>
internal sealed class HostRepository(HostsDbContext dbContext) : IHostRepository
{
    public Task<Host?> GetByIdAsync(HostId id, CancellationToken cancellationToken) =>
        dbContext.Hosts.FirstOrDefaultAsync(h => h.Id == id, cancellationToken);

    public Task<Host?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Hosts.FirstOrDefaultAsync(h => h.UserId == userId, cancellationToken);

    public Task<Host?> GetByStripeAccountIdAsync(string stripeAccountId, CancellationToken cancellationToken) =>
        dbContext.Hosts.FirstOrDefaultAsync(h => h.StripeAccountId == stripeAccountId, cancellationToken);

    public async Task<IReadOnlyList<Host>> ListForPayoutsMaintenanceAsync(DateTimeOffset staleBefore, CancellationToken cancellationToken) =>
        await dbContext.Hosts
            .Where(h => h.IsActive && h.SuspendedAt == null)
            .Where(h => !h.StripePayoutsEnabled || h.StripeSyncedAt == null || h.StripeSyncedAt < staleBefore)
            .OrderBy(h => h.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Host>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.Hosts
            .AsNoTracking()
            .OrderByDescending(h => h.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<bool> ExistsIndividualByCpfAsync(Cpf cpf, CancellationToken cancellationToken) =>
        dbContext.Hosts
            .AsNoTracking()
            .AnyAsync(h => h.Cpf == cpf && h.PersonType == PersonType.Individual && h.IdentityReleasedAt == null, cancellationToken);

    public Task<bool> ExistsByCnpjAsync(Cnpj cnpj, CancellationToken cancellationToken) =>
        dbContext.Hosts.AsNoTracking().AnyAsync(h => h.Cnpj == cnpj && h.IdentityReleasedAt == null, cancellationToken);

    public void Add(Host host) => dbContext.Hosts.Add(host);
}
