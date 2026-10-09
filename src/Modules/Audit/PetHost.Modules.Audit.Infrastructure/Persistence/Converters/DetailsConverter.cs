using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace PetHost.Modules.Audit.Infrastructure.Persistence.Converters;

/// <summary>Detalhes do registro ↔ <c>jsonb</c>.</summary>
internal sealed class DetailsConverter : ValueConverter<Dictionary<string, string?>, string>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public DetailsConverter()
        : base(
            details => JsonSerializer.Serialize(details, Json),
            json => JsonSerializer.Deserialize<Dictionary<string, string?>>(json, Json) ?? new Dictionary<string, string?>())
    {
    }

    /// <summary>Comparação por conteúdo, para o change tracker não se perder no dicionário.</summary>
    public static readonly ValueComparer<Dictionary<string, string?>> Comparer = new(
        (left, right) => JsonSerializer.Serialize(left, Json) == JsonSerializer.Serialize(right, Json),
        details => JsonSerializer.Serialize(details, Json).GetHashCode(StringComparison.Ordinal),
        details => new Dictionary<string, string?>(details, StringComparer.Ordinal));
}
