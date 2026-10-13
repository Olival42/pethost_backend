using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Hosts;
using PetHost.Shared.Contracts.Owners;

namespace PetHost.Modules.Bookings.Infrastructure.Adapters;

/// <summary>
/// Adaptador da porta <see cref="IBookingParties"/> sobre os contratos públicos:
/// <see cref="IOwnerDirectory"/> (tutor), <see cref="IHostDirectory"/> (anfitrião) e
/// <see cref="IUserDirectory"/> (nome, foto, e-mail e telefone da conta).
/// </summary>
internal sealed class BookingParties(
    IOwnerDirectory ownerDirectory,
    IHostDirectory hostDirectory,
    IUserDirectory userDirectory) : IBookingParties
{
    public async Task<PartyProfile?> FindOwnerByAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        var owner = await ownerDirectory.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);

        return owner is null ? null : new PartyProfile(owner.Id, owner.IsAvailable);
    }

    public async Task<PartyProfile?> FindHostByAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        var host = await hostDirectory.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);

        return host is null ? null : new PartyProfile(host.Id, host.IsAvailable);
    }

    public async Task<IReadOnlyDictionary<Guid, PartyInfo>> GetOwnersAsync(
        IReadOnlyCollection<Guid> ownerIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownerIds);

        if (ownerIds.Count == 0)
            return new Dictionary<Guid, PartyInfo>();

        var owners = await ownerDirectory.GetByIdsAsync(ownerIds, cancellationToken).ConfigureAwait(false);
        var accounts = await AccountsAsync([.. owners.Select(o => o.UserId)], cancellationToken).ConfigureAwait(false);

        return owners
            .Where(owner => accounts.ContainsKey(owner.UserId))
            .ToDictionary(
                owner => owner.Id,
                owner => new PartyInfo(owner.Id, accounts[owner.UserId].FullName, accounts[owner.UserId].AvatarUrl, accounts[owner.UserId].Phone, accounts[owner.UserId].Email));
    }

    public async Task<IReadOnlyDictionary<Guid, PartyInfo>> GetHostsAsync(
        IReadOnlyCollection<Guid> hostIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hostIds);

        if (hostIds.Count == 0)
            return new Dictionary<Guid, PartyInfo>();

        var hosts = await hostDirectory.GetByIdsAsync(hostIds, cancellationToken).ConfigureAwait(false);
        var accounts = await AccountsAsync([.. hosts.Select(h => h.UserId)], cancellationToken).ConfigureAwait(false);

        // Empresa aparece pelo nome fantasia; pessoa física, pelo nome da conta.
        return hosts
            .Where(host => accounts.ContainsKey(host.UserId))
            .ToDictionary(
                host => host.Id,
                host => new PartyInfo(host.Id, host.TradeName ?? accounts[host.UserId].FullName, accounts[host.UserId].AvatarUrl, accounts[host.UserId].Phone, accounts[host.UserId].Email));
    }

    /// <summary>Uma consulta ao Auth para todas as contas, não uma por pessoa.</summary>
    private async Task<Dictionary<Guid, AccountSummary>> AccountsAsync(List<Guid> userIds, CancellationToken cancellationToken)
    {
        var distinct = userIds.Distinct().ToList();
        if (distinct.Count == 0)
            return [];

        var accounts = await userDirectory.GetByIdsAsync(distinct, cancellationToken).ConfigureAwait(false);

        return accounts.ToDictionary(a => a.Id);
    }
}
