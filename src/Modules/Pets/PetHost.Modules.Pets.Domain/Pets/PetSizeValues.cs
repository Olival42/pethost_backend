namespace PetHost.Modules.Pets.Domain.Pets;

/// <summary>Valores de <see cref="PetSize"/> no banco e no JSON.</summary>
public static class PetSizeValues
{
    private static readonly Dictionary<PetSize, string> Wire = new()
    {
        [PetSize.Small] = "small",
        [PetSize.Medium] = "medium",
        [PetSize.Large] = "large",
    };

    private static readonly Dictionary<string, PetSize> FromWireValue =
        Wire.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    public static IReadOnlyList<string> All { get; } = [.. Wire.Values];

    public static string ToWire(PetSize size) => Wire[size];

    public static bool TryParse(string? value, out PetSize size) =>
        FromWireValue.TryGetValue(value?.Trim().ToLowerInvariant() ?? string.Empty, out size);
}
