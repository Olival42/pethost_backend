using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Infrastructure.Persistence.Converters;

/// <summary>
/// <see cref="PhoneNumber"/> ↔ <c>varchar</c>. <c>null</c> não passa pelo conversor:
/// coluna nula vira propriedade nula. Valor inválido no banco estoura (§6).
/// </summary>
internal sealed class PhoneNumberConverter : ValueConverter<PhoneNumber, string>
{
    public PhoneNumberConverter()
        : base(phone => phone.Value, value => FromDatabase(value))
    {
    }

    private static PhoneNumber FromDatabase(string value)
    {
        var phone = PhoneNumber.Create(value);

        return phone.IsSuccess
            ? phone.Value!
            : throw new InvalidOperationException(
                $"The stored phone is not valid: '{phone.FirstError?.Code}'. Database integrity issue.");
    }
}
