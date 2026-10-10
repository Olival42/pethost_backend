using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Pets.Domain.Pets;

namespace PetHost.Modules.Pets.Infrastructure.Persistence.Converters;

/// <summary><see cref="PetSpecies"/> ↔ texto (<c>dog</c>, <c>guinea_pig</c>...): enum como string (§11).</summary>
internal sealed class PetSpeciesConverter : ValueConverter<PetSpecies, string>
{
    public PetSpeciesConverter()
        : base(species => PetSpeciesValues.ToWire(species), value => FromDatabase(value))
    {
    }

    private static PetSpecies FromDatabase(string value) =>
        PetSpeciesValues.TryParse(value, out var species)
            ? species
            : throw new InvalidOperationException($"The stored species '{value}' is unknown. Database integrity issue.");
}
