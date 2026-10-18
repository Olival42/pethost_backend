using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Hosts;
using PetHost.Shared.Contracts.Owners;
using PetHost.Shared.Contracts.Pets;

namespace PetHost.Modules.Reviews.Infrastructure.Adapters;

/// <summary>
/// Adaptador da porta <see cref="IReviewParties"/> sobre os contratos públicos:
/// <see cref="IOwnerDirectory"/> (tutor), <see cref="IHostDirectory"/> (anfitrião),
/// <see cref="IUserDirectory"/> (nome, foto e e-mail da conta) e <see cref="IPetDirectory"/> (nome do pet).
/// </summary>
internal sealed class ReviewParties(
    IOwnerDirectory ownerDirectory,
    IHostDirectory hostDirectory,
    IUserDirectory userDirectory,
    IPetDirectory petDirectory) : IReviewParties
{
    public async Task<ReviewerProfile?> FindOwnerByAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        var owner = await ownerDirectory.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);

        return owner is null ? null : new ReviewerProfile(owner.Id, owner.IsAvailable);
    }

    public async Task<ReviewerProfile?> FindHostByAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        var host = await hostDirectory.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);

        return host is null ? null : new ReviewerProfile(host.Id, host.IsAvailable);
    }

    public async Task<IReadOnlyDictionary<Guid, ReviewPartyInfo>> GetInfoAsync(
        ReviewParty side,
        IReadOnlyCollection<Guid> profileIds,
        CancellationToken cancellationToken)
    {
        var profiles = await ProfilesAsync(side, profileIds, cancellationToken).ConfigureAwait(false);
        var accounts = await AccountsAsync(profiles, cancellationToken).ConfigureAwait(false);

        // Em público, só o primeiro nome: o sobrenome identificaria a pessoa. Empresa aparece pelo nome fantasia.
        return profiles
            .Where(p => accounts.ContainsKey(p.UserId))
            .ToDictionary(
                p => p.Id,
                p => new ReviewPartyInfo(p.Id, p.TradeName ?? FirstName(accounts[p.UserId].FullName), accounts[p.UserId].AvatarUrl));
    }

    public async Task<IReadOnlyDictionary<Guid, ReviewContact>> GetContactsAsync(
        ReviewParty side,
        IReadOnlyCollection<Guid> profileIds,
        CancellationToken cancellationToken)
    {
        var profiles = await ProfilesAsync(side, profileIds, cancellationToken).ConfigureAwait(false);
        var accounts = await AccountsAsync(profiles, cancellationToken).ConfigureAwait(false);

        return profiles
            .Where(p => accounts.ContainsKey(p.UserId))
            .ToDictionary(
                p => p.Id,
                p =>
                {
                    var account = accounts[p.UserId];
                    var reachable = p.IsAvailable && account.IsActive && account.SuspendedAt is null;

                    return new ReviewContact(p.Id, FirstName(account.FullName), account.Email, reachable);
                });
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetPetNamesAsync(
        IReadOnlyCollection<Guid> petIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(petIds);

        if (petIds.Count == 0)
            return new Dictionary<Guid, string>();

        var pets = await petDirectory.GetByIdsAsync(petIds, cancellationToken).ConfigureAwait(false);

        return pets.ToDictionary(p => p.Id, p => p.Name);
    }

    /// <summary>(perfil, conta, nome fantasia, disponível) de cada perfil do lado pedido.</summary>
    private async Task<List<(Guid Id, Guid UserId, string? TradeName, bool IsAvailable)>> ProfilesAsync(
        ReviewParty side,
        IReadOnlyCollection<Guid> profileIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileIds);

        if (profileIds.Count == 0)
            return [];

        if (side == ReviewParty.Owner)
        {
            var owners = await ownerDirectory.GetByIdsAsync(profileIds, cancellationToken).ConfigureAwait(false);
            return [.. owners.Select(o => (o.Id, o.UserId, (string?)null, o.IsAvailable))];
        }

        var hosts = await hostDirectory.GetByIdsAsync(profileIds, cancellationToken).ConfigureAwait(false);
        return [.. hosts.Select(h => (h.Id, h.UserId, h.IsCompany ? h.TradeName : null, h.IsAvailable))];
    }

    /// <summary>Uma consulta ao Auth para todas as contas, não uma por pessoa.</summary>
    private async Task<Dictionary<Guid, AccountSummary>> AccountsAsync(
        List<(Guid Id, Guid UserId, string? TradeName, bool IsAvailable)> profiles,
        CancellationToken cancellationToken)
    {
        var userIds = profiles.Select(p => p.UserId).Distinct().ToList();
        if (userIds.Count == 0)
            return [];

        var accounts = await userDirectory.GetByIdsAsync(userIds, cancellationToken).ConfigureAwait(false);

        return accounts.ToDictionary(a => a.Id);
    }

    private static string FirstName(string fullName)
    {
        var trimmed = fullName.Trim();
        var space = trimmed.IndexOf(' ', StringComparison.Ordinal);

        return space < 0 ? trimmed : trimmed[..space];
    }
}
