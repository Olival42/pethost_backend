using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Hosts;
using PetHost.Shared.Contracts.Owners;

namespace PetHost.Modules.Pets.Infrastructure.Keepers;

/// <summary>
/// Adaptador da porta <see cref="IPetKeepers"/> sobre os contratos públicos dos outros
/// módulos: <see cref="IOwnerDirectory"/> (tutor), <see cref="IHostDirectory"/> (anfitrião)
/// e <see cref="IUserDirectory"/> (nome e foto da conta).
/// </summary>
internal sealed class PetKeepers(
    IOwnerDirectory ownerDirectory,
    IHostDirectory hostDirectory,
    IUserDirectory userDirectory) : IPetKeepers
{
    public async Task<PetKeeper?> FindByAccountAsync(Guid userId, string role, CancellationToken cancellationToken)
    {
        if (role == Roles.Owner)
        {
            var owner = await ownerDirectory.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
            return owner is null ? null : PetKeeper.Owner(owner.Id);
        }

        if (role == Roles.Host)
        {
            var host = await hostDirectory.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
            return host is null ? null : PetKeeper.Host(host.Id);
        }

        // Admin não tem pets.
        return null;
    }

    public async Task<IReadOnlyDictionary<PetKeeper, KeeperSummary>> GetSummariesAsync(
        IReadOnlyCollection<PetKeeper> keepers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keepers);

        var ownerIds = keepers.Where(k => k.Type == KeeperType.Owner).Select(k => k.Id).ToList();
        var hostIds = keepers.Where(k => k.Type == KeeperType.Host).Select(k => k.Id).ToList();

        var owners = ownerIds.Count == 0
            ? []
            : await ownerDirectory.GetByIdsAsync(ownerIds, cancellationToken).ConfigureAwait(false);

        var hosts = hostIds.Count == 0
            ? []
            : await hostDirectory.GetByIdsAsync(hostIds, cancellationToken).ConfigureAwait(false);

        // Uma consulta ao Auth para todas as contas, não uma por dono.
        var userIds = owners.Select(o => o.UserId).Concat(hosts.Select(h => h.UserId)).Distinct().ToList();
        var accounts = userIds.Count == 0
            ? []
            : await userDirectory.GetByIdsAsync(userIds, cancellationToken).ConfigureAwait(false);

        var accountsById = accounts.ToDictionary(a => a.Id);
        var summaries = new Dictionary<PetKeeper, KeeperSummary>();

        foreach (var owner in owners)
        {
            if (accountsById.TryGetValue(owner.UserId, out var account))
                summaries[PetKeeper.Owner(owner.Id)] = new KeeperSummary(owner.Id, "owner", account.FullName, account.AvatarUrl);
        }

        foreach (var host in hosts)
        {
            if (accountsById.TryGetValue(host.UserId, out var account))
            {
                // Empresa aparece pelo nome fantasia; pessoa física, pelo nome da conta.
                summaries[PetKeeper.Host(host.Id)] =
                    new KeeperSummary(host.Id, "host", host.TradeName ?? account.FullName, account.AvatarUrl);
            }
        }

        return summaries;
    }

    public async Task<bool> HostExistsAsync(Guid hostId, CancellationToken cancellationToken)
    {
        var hosts = await hostDirectory.GetByIdsAsync([hostId], cancellationToken).ConfigureAwait(false);

        return hosts.Count > 0;
    }
}
