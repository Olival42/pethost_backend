using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Hosts.Domain.Hosts;

namespace PetHost.Modules.Hosts.Infrastructure.Persistence.Converters;

/// <summary>
/// <see cref="Cnpj"/> ↔ <c>char(14)</c>. Valor inválido no banco é bug de integridade,
/// não entrada de usuário: estoura na materialização (§6).
/// </summary>
internal sealed class CnpjConverter : ValueConverter<Cnpj, string>
{
    public CnpjConverter()
        : base(cnpj => cnpj.Value, value => FromDatabase(value))
    {
    }

    private static Cnpj FromDatabase(string value)
    {
        var cnpj = Cnpj.Create(value);

        return cnpj.IsSuccess
            ? cnpj.Value!
            : throw new InvalidOperationException(
                $"The stored CNPJ is not valid: '{cnpj.FirstError?.Code}'. Database integrity issue.");
    }
}
