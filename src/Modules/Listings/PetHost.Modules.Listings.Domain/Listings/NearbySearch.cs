namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>Busca por raio: os cantinhos ativos a até <paramref name="RadiusKm"/> de <paramref name="Origin"/>.</summary>
/// <param name="ExcludedHostIds">Anfitriões que não aparecem (inativos ou suspensos).</param>
/// <param name="Page">Começa em 1.</param>
public sealed record NearbySearch(
    GeoPoint Origin,
    int RadiusKm,
    IReadOnlyCollection<Guid> ExcludedHostIds,
    int Page,
    int PageSize);

/// <summary>Um cantinho achado na busca, com a distância até a origem.</summary>
public sealed record NearbyListing(Listing Listing, double DistanceKm);
