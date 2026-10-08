using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Infrastructure.Persistence.Converters;

/// <summary><see cref="PasswordHash"/> ↔ <c>varchar</c>.</summary>
internal sealed class PasswordHashConverter : ValueConverter<PasswordHash, string>
{
    public PasswordHashConverter()
        : base(hash => hash.Value, value => FromDatabase(value))
    {
    }

    private static PasswordHash FromDatabase(string value)
    {
        var hash = PasswordHash.FromHash(value);

        return hash.IsSuccess
            ? hash.Value!
            : throw new InvalidOperationException(
                "The stored password hash is not valid. Database integrity issue.");
    }
}
