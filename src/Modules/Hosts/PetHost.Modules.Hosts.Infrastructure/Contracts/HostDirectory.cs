using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Modules.Hosts.Infrastructure.Persistence;
using PetHost.Shared.Contracts.Hosts;

namespace PetHost.Modules.Hosts.Infrastructure.Contracts;

/// <summary>Adaptador do contrato <see cref="IHostDirectory"/> (§5): leitura direta da tabela.</summary>
internal sealed class HostDirectory(HostsDbContext dbContext) : IHostDirectory
{
    public async Task<HostReference?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var host = await dbContext.Hosts
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        return host is null ? null : ToReference(host);
    }

    public async Task<IReadOnlyList<HostReference>> GetByIdsAsync(
        IReadOnlyCollection<Guid> hostIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hostIds);

        if (hostIds.Count == 0)
            return [];

        var ids = hostIds.Select(id => new HostId(id)).ToList();

        var hosts = await dbContext.Hosts
            .AsNoTracking()
            .Where(h => ids.Contains(h.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. hosts.Select(ToReference)];
    }

    private static HostReference ToReference(Host host) =>
        new(host.Id.Value, host.UserId, PersonTypeValues.ToWire(host.PersonType), host.TradeName, host.IsActive);
}
