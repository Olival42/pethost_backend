using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Hosts.Domain.Hosts;

namespace PetHost.Modules.Hosts.Infrastructure.Persistence.Converters;

/// <summary>
/// <see cref="Cpf"/> ↔ <c>char(11)</c>. Valor inválido no banco é bug de integridade,
/// não entrada de usuário: estoura na materialização (§6).
/// </summary>
internal sealed class CpfConverter : ValueConverter<Cpf, string>
{
    public CpfConverter()
        : base(cpf => cpf.Value, value => FromDatabase(value))
    {
    }

    private static Cpf FromDatabase(string value)
    {
        var cpf = Cpf.Create(value);

        return cpf.IsSuccess
            ? cpf.Value!
            : throw new InvalidOperationException(
                $"The stored CPF is not valid: '{cpf.FirstError?.Code}'. Database integrity issue.");
    }
}
