using PetHost.Modules.Payments.Application.Abstractions;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Hosts;
using PetHost.Shared.Contracts.Owners;

namespace PetHost.Modules.Payments.Infrastructure.Adapters;

/// <summary>
/// Adaptador da porta <see cref="IPaymentParties"/> sobre os contratos públicos
/// <see cref="IOwnerDirectory"/> (tutor), <see cref="IHostDirectory"/> (anfitrião e conta conectada)
/// e <see cref="IUserDirectory"/> (nome e e-mail da conta).
/// </summary>
internal sealed class PaymentParties(
    IOwnerDirectory ownerDirectory,
    IHostDirectory hostDirectory,
    IUserDirectory userDirectory) : IPaymentParties
{
    public async Task<Guid?> FindOwnerIdAsync(Guid userId, CancellationToken cancellationToken) =>
        (await ownerDirectory.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false))?.Id;

    public async Task<Guid?> FindHostIdAsync(Guid userId, CancellationToken cancellationToken) =>
        (await hostDirectory.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false))?.Id;

    public async Task<PartyContact?> GetOwnerContactAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var owners = await ownerDirectory.GetByIdsAsync([ownerId], cancellationToken).ConfigureAwait(false);

        return owners.Count == 0 ? null : await ContactAsync(owners[0].UserId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PartyContact?> GetHostContactAsync(Guid hostId, CancellationToken cancellationToken)
    {
        var hosts = await hostDirectory.GetByIdsAsync([hostId], cancellationToken).ConfigureAwait(false);

        return hosts.Count == 0 ? null : await ContactAsync(hosts[0].UserId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetHostPayoutAccountAsync(Guid hostId, CancellationToken cancellationToken)
    {
        var hosts = await hostDirectory.GetByIdsAsync([hostId], cancellationToken).ConfigureAwait(false);

        return hosts.Count == 0 ? null : hosts[0].StripeAccountId;
    }

    private async Task<PartyContact?> ContactAsync(Guid userId, CancellationToken cancellationToken)
    {
        var accounts = await userDirectory.GetByIdsAsync([userId], cancellationToken).ConfigureAwait(false);

        return accounts.Count == 0 ? null : new PartyContact(accounts[0].FullName, accounts[0].Email);
    }
}
