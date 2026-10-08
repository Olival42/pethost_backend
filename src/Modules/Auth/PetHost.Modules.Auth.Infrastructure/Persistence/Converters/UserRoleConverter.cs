using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Infrastructure.Persistence.Converters;

/// <summary>
/// <see cref="UserRole"/> ↔ <c>varchar</c> minúsculo (<c>owner</c>, <c>host</c>,
/// <c>admin</c>), conforme a seção 7 do dicionário. Guardar o texto, e não o
/// número do enum, mantém o banco legível e imune a reordenação do enum.
/// </summary>
internal sealed class UserRoleConverter : ValueConverter<UserRole, string>
{
    public UserRoleConverter()
        : base(role => UserRoleValues.ToWire(role), value => FromDatabase(value))
    {
    }

    private static UserRole FromDatabase(string value) =>
        UserRoleValues.TryParse(value, out var role)
            ? role
            : throw new InvalidOperationException(
                $"The stored role '{value}' is not a known user role. Database integrity issue.");
}
