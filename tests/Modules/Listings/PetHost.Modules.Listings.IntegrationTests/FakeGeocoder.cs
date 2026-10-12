using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.IntegrationTests;

/// <summary>
/// Mapa de mentira para os testes: o ponto sai do bairro. Distâncias reais aproximadas a partir
/// da Zona 7 (Maringá): Zona 5 ~1 km, Vila Sarandi ~7 km, Centro de Londrina ~80 km. Bairro
/// "Fora do Mapa" não é achado; "Mapa Fora do Ar" simula o serviço caído.
/// </summary>
internal sealed class FakeGeocoder : IGeocoder
{
    public const string NotFoundNeighborhood = "Fora do Mapa";
    public const string UnavailableNeighborhood = "Mapa Fora do Ar";

    private static readonly Dictionary<string, (decimal Latitude, decimal Longitude)> Points = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Zona 7"] = (-23.410000m, -51.933000m),
        ["Zona 5"] = (-23.418000m, -51.925000m),
        ["Vila Sarandi"] = (-23.443600m, -51.874000m),
        ["Centro de Londrina"] = (-23.310400m, -51.162800m),
    };

    public Task<GeocodingResult> LocateAsync(ListingAddressData address, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);

        var neighborhood = address.Neighborhood?.Trim() ?? string.Empty;

        var result = neighborhood == UnavailableNeighborhood
            ? GeocodingResult.Unavailable
            : Points.TryGetValue(neighborhood, out var point)
                ? GeocodingResult.Found(point.Latitude, point.Longitude)
                : GeocodingResult.NotFound;

        return Task.FromResult(result);
    }
}
