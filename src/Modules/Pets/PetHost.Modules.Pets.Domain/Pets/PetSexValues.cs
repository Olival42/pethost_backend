namespace PetHost.Modules.Pets.Domain.Pets;

/// <summary>Valores de <see cref="PetSex"/> no banco e no JSON.</summary>
public static class PetSexValues
{
    private static readonly Dictionary<PetSex, string> Wire = new()
    {
        [PetSex.Male] = "male",
        [PetSex.Female] = "female",
        [PetSex.Unknown] = "unknown",
    };

    private static readonly Dictionary<string, PetSex> FromWireValue =
        Wire.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    public static IReadOnlyList<string> All { get; } = [.. Wire.Values];

    public static string ToWire(PetSex sex) => Wire[sex];

    public static bool TryParse(string? value, out PetSex sex) =>
        FromWireValue.TryGetValue(value?.Trim().ToLowerInvariant() ?? string.Empty, out sex);
}
