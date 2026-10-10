using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Pets.Domain.Pets;

namespace PetHost.Modules.Pets.Infrastructure.Persistence.Converters;

/// <summary><see cref="PetSize"/> ↔ texto (<c>small</c>, <c>medium</c>, <c>large</c>).</summary>
internal sealed class PetSizeConverter : ValueConverter<PetSize, string>
{
    public PetSizeConverter()
        : base(size => PetSizeValues.ToWire(size), value => FromDatabase(value))
    {
    }

    private static PetSize FromDatabase(string value) =>
        PetSizeValues.TryParse(value, out var size)
            ? size
            : throw new InvalidOperationException($"The stored size '{value}' is unknown. Database integrity issue.");
}
