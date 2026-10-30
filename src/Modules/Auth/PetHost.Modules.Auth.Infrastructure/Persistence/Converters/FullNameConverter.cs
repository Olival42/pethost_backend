using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Infrastructure.Persistence.Converters;

/// <summary>
/// <see cref="FullName"/> ↔ <c>varchar</c>. Valor inválido no banco é bug de
/// integridade, não entrada de usuário: estoura na materialização (§6).
/// </summary>
internal sealed class FullNameConverter : ValueConverter<FullName, string>
{
    public FullNameConverter()
        : base(name => name.Value, value => FromDatabase(value))
    {
    }

    private static FullName FromDatabase(string value)
    {
        var name = FullName.Create(value);

        return name.IsSuccess
            ? name.Value!
            : throw new InvalidOperationException(
                $"The stored full name is not valid: '{name.FirstError?.Code}'. Database integrity issue.");
    }
}
