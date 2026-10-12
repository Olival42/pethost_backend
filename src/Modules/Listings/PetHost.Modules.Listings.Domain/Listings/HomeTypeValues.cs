namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>Valores de <see cref="HomeType"/> no banco e no JSON.</summary>
public static class HomeTypeValues
{
    private static readonly Dictionary<HomeType, string> Wire = new()
    {
        [HomeType.House] = "house",
        [HomeType.Apartment] = "apartment",
    };

    private static readonly Dictionary<string, HomeType> FromWireValue =
        Wire.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    /// <summary>Todos os valores aceitos, na ordem do enum.</summary>
    public static IReadOnlyList<string> All { get; } = [.. Wire.Values];

    public static string ToWire(HomeType homeType) => Wire[homeType];

    /// <summary>Aceita maiúsculas/minúsculas e espaços nas pontas.</summary>
    public static bool TryParse(string? value, out HomeType homeType) =>
        FromWireValue.TryGetValue(value?.Trim().ToLowerInvariant() ?? string.Empty, out homeType);
}
