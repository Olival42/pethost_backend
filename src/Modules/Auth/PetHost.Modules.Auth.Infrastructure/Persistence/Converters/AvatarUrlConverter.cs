using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Infrastructure.Persistence.Converters;

/// <summary>
/// <see cref="AvatarUrl"/> ↔ <c>varchar</c>. <c>null</c> não passa pelo conversor.
/// Valor inválido no banco estoura (§6).
/// </summary>
internal sealed class AvatarUrlConverter : ValueConverter<AvatarUrl, string>
{
    public AvatarUrlConverter()
        : base(url => url.Value, value => FromDatabase(value))
    {
    }

    private static AvatarUrl FromDatabase(string value)
    {
        var url = AvatarUrl.Create(value);

        return url.IsSuccess
            ? url.Value!
            : throw new InvalidOperationException(
                $"The stored avatar URL is not valid: '{url.FirstError?.Code}'. Database integrity issue.");
    }
}
