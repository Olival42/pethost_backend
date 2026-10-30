using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Contracts.Accounts;

namespace PetHost.Modules.Auth.Application.Sessions.Responses;

/// <summary><see cref="Address"/> do domínio → <see cref="AddressData"/> do JSON.</summary>
public static class AddressMapping
{
    public static AddressData? ToAddressData(Address? address) =>
        address is null
            ? null
            : new AddressData(
                address.ZipCode.Value,
                address.Street,
                address.Number,
                address.Complement,
                address.Neighborhood,
                address.City,
                address.State.Value);
}
