using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Hosts;

namespace PetHost.Modules.Listings.Infrastructure.Hosts;

/// <summary>
/// Adaptador da porta <see cref="IListingHosts"/> sobre os contratos públicos:
/// <see cref="IHostDirectory"/> (anfitrião) e <see cref="IUserDirectory"/> (nome e foto da conta).
/// </summary>
internal sealed class ListingHosts(
    IHostDirectory hostDirectory,
    IUserDirectory userDirectory) : IListingHosts
{
    public async Task<HostProfile?> FindByAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        var host = await hostDirectory.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);

        return host is null ? null : new HostProfile(host.Id, host.IsCompany, host.IsAvailable);
    }

    public async Task<IReadOnlyDictionary<Guid, ListingHostSummary>> GetSummariesAsync(
        IReadOnlyCollection<Guid> hostIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hostIds);

        if (hostIds.Count == 0)
            return new Dictionary<Guid, ListingHostSummary>();

        var hosts = await hostDirectory.GetByIdsAsync(hostIds, cancellationToken).ConfigureAwait(false);

        // Uma consulta ao Auth para todas as contas, não uma por anfitrião.
        var userIds = hosts.Select(h => h.UserId).Distinct().ToList();
        var accounts = userIds.Count == 0
            ? []
            : await userDirectory.GetByIdsAsync(userIds, cancellationToken).ConfigureAwait(false);

        var accountsById = accounts.ToDictionary(a => a.Id);
        var summaries = new Dictionary<Guid, ListingHostSummary>();

        foreach (var host in hosts)
        {
            if (!accountsById.TryGetValue(host.UserId, out var account))
                continue;

            // Empresa aparece pelo nome fantasia; pessoa física, pelo nome da conta.
            summaries[host.Id] = new ListingHostSummary(
                host.Id,
                host.TradeName ?? account.FullName,
                account.AvatarUrl,
                host.IsCompany,
                host.IsAvailable);
        }

        return summaries;
    }

    public Task<IReadOnlyList<Guid>> GetUnavailableIdsAsync(CancellationToken cancellationToken) =>
        hostDirectory.GetUnavailableIdsAsync(cancellationToken);
}
