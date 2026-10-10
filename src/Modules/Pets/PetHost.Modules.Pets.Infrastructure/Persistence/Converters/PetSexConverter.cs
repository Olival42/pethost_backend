using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Pets.Domain.Pets;

namespace PetHost.Modules.Pets.Infrastructure.Persistence.Converters;

/// <summary><see cref="PetSex"/> ↔ texto (<c>male</c>, <c>female</c>, <c>unknown</c>).</summary>
internal sealed class PetSexConverter : ValueConverter<PetSex, string>
{
    public PetSexConverter()
        : base(sex => PetSexValues.ToWire(sex), value => FromDatabase(value))
    {
    }

    private static PetSex FromDatabase(string value) =>
        PetSexValues.TryParse(value, out var sex)
            ? sex
            : throw new InvalidOperationException($"The stored sex '{value}' is unknown. Database integrity issue.");
}
