using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Accounts;

namespace PetHost.Modules.Listings.Infrastructure.Accounts;

/// <summary>Adaptador da porta <see cref="IAccountAddresses"/> sobre <see cref="IUserDirectory"/> (Auth).</summary>
internal sealed class AccountAddresses(IUserDirectory userDirectory) : IAccountAddresses
{
    public async Task<ListingAddressData?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var accounts = await userDirectory.GetByIdsAsync([userId], cancellationToken).ConfigureAwait(false);
        var address = accounts.Count > 0 ? accounts[0].Address : null;

        // Sem cidade não há o que procurar no mapa.
        if (address is null || string.IsNullOrWhiteSpace(address.City))
            return null;

        return new ListingAddressData(
            address.ZipCode,
            address.Street,
            address.Number,
            address.Complement,
            address.Neighborhood,
            address.City,
            address.State);
    }
}
