using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Infrastructure.Persistence.Converters;

/// <summary>
/// <see cref="Email"/> ↔ <c>varchar</c>. Valor inválido no banco é bug de
/// integridade, não entrada de usuário: estoura na materialização (§6).
/// </summary>
internal sealed class EmailConverter : ValueConverter<Email, string>
{
    public EmailConverter()
        : base(email => email.Value, value => FromDatabase(value))
    {
    }

    private static Email FromDatabase(string value)
    {
        var email = Email.Create(value);

        return email.IsSuccess
            ? email.Value!
            : throw new InvalidOperationException(
                $"The stored email is not valid: '{email.FirstError?.Code}'. Database integrity issue.");
    }
}
