using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Infrastructure.Geocoding;

/// <summary>
/// Geocodificador sobre o Nominatim (OpenStreetMap). O mapa do OSM no Brasil nem sempre tem o
/// número da casa ou o CEP, então tenta do mais preciso para o mais grosso e fica com o
/// primeiro que achar: rua e número → rua no bairro → bairro → CEP. O "bairro" basta para
/// "2 km de você".
/// </summary>
/// <remarks>Falha de rede, timeout ou resposta de erro viram <see cref="GeocodingStatus.Unavailable"/> (com log), nunca exceção.</remarks>
internal sealed partial class NominatimGeocoder(
    HttpClient httpClient,
    GeocodingRateGate rateGate,
    IOptions<GeocodingOptions> options,
    ILogger<NominatimGeocoder> logger) : IGeocoder
{
    private static readonly Dictionary<string, string> StateNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AC"] = "Acre", ["AL"] = "Alagoas", ["AM"] = "Amazonas", ["AP"] = "Amapá", ["BA"] = "Bahia",
        ["CE"] = "Ceará", ["DF"] = "Distrito Federal", ["ES"] = "Espírito Santo", ["GO"] = "Goiás",
        ["MA"] = "Maranhão", ["MG"] = "Minas Gerais", ["MS"] = "Mato Grosso do Sul", ["MT"] = "Mato Grosso",
        ["PA"] = "Pará", ["PB"] = "Paraíba", ["PE"] = "Pernambuco", ["PI"] = "Piauí", ["PR"] = "Paraná",
        ["RJ"] = "Rio de Janeiro", ["RN"] = "Rio Grande do Norte", ["RO"] = "Rondônia", ["RR"] = "Roraima",
        ["RS"] = "Rio Grande do Sul", ["SC"] = "Santa Catarina", ["SE"] = "Sergipe", ["SP"] = "São Paulo",
        ["TO"] = "Tocantins",
    };

    public async Task<GeocodingResult> LocateAsync(ListingAddressData address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);

        foreach (var query in Queries(address))
        {
            var result = await rateGate
                .RunAsync(ct => SearchAsync(query, ct), cancellationToken)
                .ConfigureAwait(false);

            if (result.Status != GeocodingStatus.NotFound)
                return result;
        }

        return GeocodingResult.NotFound;
    }

    /// <summary>As tentativas, da mais precisa para a mais grossa. Tentativa sem dado suficiente é pulada.</summary>
    private IEnumerable<string> Queries(ListingAddressData address)
    {
        var city = Clean(address.City);
        var state = Clean(address.State) is { } uf ? StateNames.GetValueOrDefault(uf, uf) : null;
        var street = Clean(address.Street);
        var neighborhood = Clean(address.Neighborhood);
        var number = Clean(address.Number) is { } n && n.Any(char.IsAsciiDigit) ? n : null;

        if (city is null)
            yield break;

        if (street is not null)
        {
            yield return Structured(
                ("street", number is null ? street : $"{number} {street}"),
                ("city", city),
                ("state", state));

            yield return FreeText(street, neighborhood, city, state);
        }

        if (neighborhood is not null)
            yield return FreeText(neighborhood, city, state);

        var zipDigits = new string([.. (address.ZipCode ?? string.Empty).Where(char.IsAsciiDigit)]);
        if (zipDigits.Length == ListingAddress.ZipCodeLength)
            yield return Structured(("postalcode", $"{zipDigits[..5]}-{zipDigits[5..]}"));
    }

    private string Structured(params (string Key, string? Value)[] parts) =>
        BuildQuery([.. parts.Where(part => part.Value is not null).Select(part => (part.Key, part.Value ?? string.Empty))]);

    private string FreeText(params string?[] parts) =>
        BuildQuery([("q", string.Join(", ", parts.Where(part => part is not null)))]);

    private string BuildQuery(List<(string Key, string Value)> parameters)
    {
        parameters.Add(("format", "jsonv2"));
        parameters.Add(("limit", "1"));
        parameters.Add(("countrycodes", "br"));

        if (Clean(options.Value.Email) is { } email)
            parameters.Add(("email", email));

        return "search?" + string.Join("&", parameters.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value)}"));
    }

    private async Task<GeocodingResult> SearchAsync(string query, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(new Uri(query, UriKind.Relative), cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                LogServiceError((int)response.StatusCode);
                return GeocodingResult.Unavailable;
            }

            var places = await response.Content
                .ReadFromJsonAsync<List<NominatimPlace>>(cancellationToken)
                .ConfigureAwait(false);

            var place = places is { Count: > 0 } ? places[0] : null;
            if (place is null
                || !decimal.TryParse(place.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
                || !decimal.TryParse(place.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
            {
                return GeocodingResult.NotFound;
            }

            return GeocodingResult.Found(latitude, longitude);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException
            && !cancellationToken.IsCancellationRequested)
        {
            LogServiceUnreachable(exception);
            return GeocodingResult.Unavailable;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geocoding service answered with status {StatusCode}.")]
    private partial void LogServiceError(int statusCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Geocoding service is unreachable.")]
    private partial void LogServiceUnreachable(Exception exception);

    /// <summary>Um resultado do Nominatim: só o que usamos.</summary>
    private sealed record NominatimPlace(
        [property: JsonPropertyName("lat")] string? Lat,
        [property: JsonPropertyName("lon")] string? Lon);
}
