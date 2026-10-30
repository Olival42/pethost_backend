namespace PetHost.Modules.Hosts.Domain.HostApplications;

/// <summary>
/// Tradução enum ↔ texto (banco e JSON) dos enums da solicitação. Cada enum tem a sua
/// classe <c>...Values</c>, que usa esta por dentro.
/// </summary>
internal sealed class WireValues<TEnum>(IReadOnlyDictionary<TEnum, string> wire)
    where TEnum : struct, Enum
{
    private readonly Dictionary<string, TEnum> _fromWire =
        wire.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    public IReadOnlyList<string> All { get; } = [.. wire.Values];

    public string ToWire(TEnum value) => wire[value];

    /// <summary>Lê o valor do banco: valor desconhecido é bug de integridade.</summary>
    public TEnum FromWire(string value) =>
        _fromWire.TryGetValue(value, out var parsed)
            ? parsed
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"Unknown {typeof(TEnum).Name}.");

    /// <summary>Lê o valor vindo do cliente: aparado, sem diferenciar maiúsculas.</summary>
    public bool TryParse(string? value, out TEnum parsed) =>
        _fromWire.TryGetValue(value?.Trim().ToLowerInvariant() ?? string.Empty, out parsed);
}
