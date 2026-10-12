using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Infrastructure.Persistence.Repositories;

/// <summary>Implementação de <see cref="IListingRepository"/> sobre o EF Core.</summary>
internal sealed class ListingRepository(ListingsDbContext dbContext) : IListingRepository
{
    /// <summary>Quilômetros por grau de latitude (aproximado, vale em qualquer lugar).</summary>
    private const double KmPerDegree = 111.32;

    private const double DegreesToRadians = Math.PI / 180;

    public Task<Listing?> GetByIdAsync(ListingId id, CancellationToken cancellationToken) =>
        dbContext.Listings
            .Include(ListingsDbContext.PhotosField)
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Listing>> ListByHostAsync(Guid hostId, CancellationToken cancellationToken) =>
        await dbContext.Listings
            .AsNoTracking()
            .Include(ListingsDbContext.PhotosField)
            .Where(l => l.HostId == hostId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<int> CountByHostAsync(Guid hostId, CancellationToken cancellationToken) =>
        dbContext.Listings.CountAsync(l => l.HostId == hostId, cancellationToken);

    /// <summary>
    /// Duas etapas: (1) no banco, filtra pela caixa em volta do círculo (usa o índice em
    /// latitude/longitude), calcula a distância exata (haversine) e pagina pela distância;
    /// (2) carrega só os cantinhos da página, com as fotos.
    /// </summary>
    public async Task<(IReadOnlyList<NearbyListing> Items, long TotalCount)> SearchNearbyAsync(
        NearbySearch search,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);

        var originLat = (double)search.Origin.Latitude;
        var originLng = (double)search.Origin.Longitude;
        var originLatRad = originLat * DegreesToRadians;
        var originLngRad = originLng * DegreesToRadians;
        var cosOriginLat = Math.Cos(originLatRad);
        var radiusKm = (double)search.RadiusKm;

        // Caixa delimitadora: grosso, mas barata e indexada. O círculo exato vem no haversine.
        var latDelta = (decimal)(radiusKm / KmPerDegree);
        var lngDelta = (decimal)(radiusKm / (KmPerDegree * Math.Max(cosOriginLat, 0.01)));
        var minLat = search.Origin.Latitude - latDelta;
        var maxLat = search.Origin.Latitude + latDelta;
        var minLng = search.Origin.Longitude - lngDelta;
        var maxLng = search.Origin.Longitude + lngDelta;

        var excluded = search.ExcludedHostIds.ToList();

        var withinRadius = dbContext.Listings
            .AsNoTracking()
            .Where(l => l.IsActive && !excluded.Contains(l.HostId))
            .Where(l => l.Location.Latitude >= minLat && l.Location.Latitude <= maxLat)
            .Where(l => l.Location.Longitude >= minLng && l.Location.Longitude <= maxLng)
            .Select(l => new
            {
                l.Id,
                l.CreatedAt,
                // Haversine; LEAST(1, ...) protege o asin de um arredondamento acima de 1.
                DistanceKm = 2 * GeoPoint.EarthRadiusKm * Math.Asin(Math.Min(1.0, Math.Sqrt(
                    Math.Pow(Math.Sin((((double)l.Location.Latitude * DegreesToRadians) - originLatRad) / 2), 2)
                    + (cosOriginLat
                        * Math.Cos((double)l.Location.Latitude * DegreesToRadians)
                        * Math.Pow(Math.Sin((((double)l.Location.Longitude * DegreesToRadians) - originLngRad) / 2), 2))))),
            })
            .Where(x => x.DistanceKm <= radiusKm);

        var total = await withinRadius.LongCountAsync(cancellationToken).ConfigureAwait(false);
        if (total == 0)
            return ([], 0);

        var page = await withinRadius
            .OrderBy(x => x.DistanceKm)
            .ThenByDescending(x => x.CreatedAt)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .Select(x => new { x.Id, x.DistanceKm })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ids = page.Select(x => x.Id).ToList();
        var listings = await dbContext.Listings
            .AsNoTracking()
            .Include(ListingsDbContext.PhotosField)
            .Where(l => ids.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, cancellationToken)
            .ConfigureAwait(false);

        // Mantém a ordem da distância; um cantinho desativado entre as duas consultas sai da página.
        var items = page
            .Where(x => listings.ContainsKey(x.Id))
            .Select(x => new NearbyListing(listings[x.Id], x.DistanceKm))
            .ToList();

        return (items, total);
    }

    public void Add(Listing listing) => dbContext.Listings.Add(listing);
}
