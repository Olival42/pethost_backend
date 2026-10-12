namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>Valores de <see cref="AcceptedSpecies"/> no JSON (<c>snake_case</c>, iguais a <c>pets.species</c>).</summary>
public static class AcceptedSpeciesValues
{
    private static readonly (AcceptedSpecies Species, string Wire)[] Wire =
    [
        (AcceptedSpecies.Dog, "dog"),
        (AcceptedSpecies.Cat, "cat"),
        (AcceptedSpecies.Cockatiel, "cockatiel"),
        (AcceptedSpecies.Parrot, "parrot"),
        (AcceptedSpecies.Parakeet, "parakeet"),
        (AcceptedSpecies.Canary, "canary"),
        (AcceptedSpecies.Rabbit, "rabbit"),
        (AcceptedSpecies.Hamster, "hamster"),
        (AcceptedSpecies.GuineaPig, "guinea_pig"),
        (AcceptedSpecies.Fish, "fish"),
        (AcceptedSpecies.Turtle, "turtle"),
        (AcceptedSpecies.Exotic, "exotic"),
    ];

    /// <summary>Todos os valores aceitos, na ordem do enum.</summary>
    public static IReadOnlyList<string> All { get; } = [.. Wire.Select(pair => pair.Wire)];

    /// <summary>Todos os bits juntos: o maior valor que a coluna aceita.</summary>
    public static AcceptedSpecies Every { get; } = Wire.Aggregate(AcceptedSpecies.None, (all, pair) => all | pair.Species);

    /// <summary>O conjunto como lista, na ordem do enum.</summary>
    public static IReadOnlyList<string> ToWire(AcceptedSpecies species) =>
        [.. Wire.Where(pair => species.HasFlag(pair.Species)).Select(pair => pair.Wire)];

    /// <summary>
    /// Junta os valores num conjunto. Aceita maiúsculas/minúsculas, espaços nas pontas e
    /// repetição. Falso se algum valor não é conhecido.
    /// </summary>
    public static bool TryParse(IEnumerable<string?> values, out AcceptedSpecies species)
    {
        ArgumentNullException.ThrowIfNull(values);

        species = AcceptedSpecies.None;

        foreach (var value in values)
        {
            var text = value?.Trim().ToLowerInvariant();
            var match = Array.Find(Wire, pair => pair.Wire == text);

            if (match.Wire is null)
                return false;

            species |= match.Species;
        }

        return true;
    }
}
