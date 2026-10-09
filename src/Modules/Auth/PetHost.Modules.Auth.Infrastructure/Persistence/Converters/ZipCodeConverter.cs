using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Infrastructure.Persistence.Converters;

/// <summary>
/// <see cref="ZipCode"/> ↔ <c>char</c>. Valor inválido no banco é bug de
/// integridade, não entrada de usuário: estoura na materialização (§6).
/// </summary>
internal sealed class ZipCodeConverter : ValueConverter<ZipCode, string>
{
    public ZipCodeConverter()
        : base(value => value.Value, value => FromDatabase(value))
    {
    }

    private static ZipCode FromDatabase(string value)
    {
        var result = ZipCode.Create(value);

        return result.IsSuccess
            ? result.Value!
            : throw new InvalidOperationException(
                $"The stored zip code is not valid: '{result.FirstError?.Code}'. Database integrity issue.");
    }
}
