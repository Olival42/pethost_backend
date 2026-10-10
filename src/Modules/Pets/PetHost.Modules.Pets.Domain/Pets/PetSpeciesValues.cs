namespace PetHost.Modules.Pets.Domain.Pets;

/// <summary>Valores de <see cref="PetSpecies"/> no banco e no JSON (<c>snake_case</c>).</summary>
public static class PetSpeciesValues
{
    private static readonly Dictionary<PetSpecies, string> Wire = new()
    {
        [PetSpecies.Dog] = "dog",
        [PetSpecies.Cat] = "cat",
        [PetSpecies.Cockatiel] = "cockatiel",
        [PetSpecies.Parrot] = "parrot",
        [PetSpecies.Parakeet] = "parakeet",
        [PetSpecies.Canary] = "canary",
        [PetSpecies.Rabbit] = "rabbit",
        [PetSpecies.Hamster] = "hamster",
        [PetSpecies.GuineaPig] = "guinea_pig",
        [PetSpecies.Fish] = "fish",
        [PetSpecies.Turtle] = "turtle",
        [PetSpecies.Exotic] = "exotic",
    };

    private static readonly Dictionary<string, PetSpecies> FromWireValue =
        Wire.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    /// <summary>Todos os valores aceitos, na ordem do enum.</summary>
    public static IReadOnlyList<string> All { get; } = [.. Wire.Values];

    public static string ToWire(PetSpecies species) => Wire[species];

    /// <summary>Aceita maiúsculas/minúsculas e espaços nas pontas.</summary>
    public static bool TryParse(string? value, out PetSpecies species) =>
        FromWireValue.TryGetValue(value?.Trim().ToLowerInvariant() ?? string.Empty, out species);
}
