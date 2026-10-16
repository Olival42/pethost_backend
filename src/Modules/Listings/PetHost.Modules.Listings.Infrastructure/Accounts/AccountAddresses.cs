using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Hosts;

namespace PetHost.Modules.Listings.Infrastructure.Accounts;

/// <summary>
/// Adaptador da porta <see cref="IAccountAddresses"/>: o endereço da empresa por
/// <see cref="IHostDirectory"/> (Hosts) quando a empresa está logada como anfitrião; senão, o da
/// conta por <see cref="IUserDirectory"/> (Auth).
/// </summary>
internal sealed class AccountAddresses(IUserDirectory userDirectory, IHostDirectory hostDirectory) : IAccountAddresses
{
    public async Task<ListingAddressData?> GetAsync(Guid userId, string? role, CancellationToken cancellationToken)
    {
        if (role == Roles.Host
            && await hostDirectory.GetCompanyAddressByUserIdAsync(userId, cancellationToken).ConfigureAwait(false) is { } company)
            return ToListingAddress(company);

        var accounts = await userDirectory.GetByIdsAsync([userId], cancellationToken).ConfigureAwait(false);

        return ToListingAddress(accounts.Count > 0 ? accounts[0].Address : null);
    }

    private static ListingAddressData? ToListingAddress(AddressData? address)
    {
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
