using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Infrastructure.Persistence.Repositories;

/// <summary>Implementação de <see cref="IListingRepository"/> sobre o EF Core.</summary>
internal sealed class ListingRepository(ListingsDbContext dbContext) : IListingRepository
{
    /// <summary>Quilômetros por grau de latitude (aproximado, vale em qualquer lugar).</summary>
    private const double KmPerDegree = 111.32;

    private const double DegreesToRadians = Math.PI / 180;

    /// <summary>Caractere de escape do <c>ILIKE</c> nos filtros de texto.</summary>
    private const string LikeEscape = "\\";

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
    /// Duas etapas: (1) no banco, aplica os filtros e, na busca por raio, a caixa em volta do
    /// círculo (usa o índice em latitude/longitude) e a distância exata (haversine); conta e
    /// pagina; (2) carrega só os cantinhos da página, com as fotos.
    /// </summary>
    public async Task<(IReadOnlyList<FoundListing> Items, long TotalCount)> SearchAsync(
        ListingSearch search,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);

        var excluded = search.ExcludedHostIds.ToList();
        var matching = Filter(
            dbContext.Listings.AsNoTracking().Where(l => l.IsActive && !excluded.Contains(l.HostId)),
            search.Filters);

        // Sem vaga nas datas da busca.
        if (search.ExcludedListingIds is { Count: > 0 } full)
        {
            var fullIds = full.ToList();
            matching = matching.Where(l => !fullIds.Contains(l.Id));
        }

        // Algum dia do período (da chegada à saída, inclusive) bloqueado na agenda.
        if (search.Stay is { } stay)
        {
            matching = matching.Where(l => !dbContext.AvailabilityBlocks.Any(
                b => b.ListingId == l.Id && b.StartDate <= stay.CheckOut && b.EndDate >= stay.CheckIn));
        }

        var ranked = search.Area is { } area
            ? WithinRadius(matching, area)
            : matching
                .OrderByDescending(l => l.CreatedAt)
                .ThenBy(l => l.Id)
                .Select(l => new Ranked { Id = l.Id, DistanceKm = null });

        var total = await ranked.LongCountAsync(cancellationToken).ConfigureAwait(false);
        if (total == 0)
            return ([], 0);

        var page = await ranked
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ids = page.Select(x => x.Id).ToList();
        var listings = await dbContext.Listings
            .AsNoTracking()
            .Include(ListingsDbContext.PhotosField)
            .Where(l => ids.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, cancellationToken)
            .ConfigureAwait(false);

        // Mantém a ordem da página; um cantinho desativado entre as duas consultas sai dela.
        var items = page
            .Where(x => listings.ContainsKey(x.Id))
            .Select(x => new FoundListing(listings[x.Id], x.DistanceKm))
            .ToList();

        return (items, total);
    }

    public async Task<IReadOnlyDictionary<ListingId, int>> GetCapacitiesAsync(
        IReadOnlyCollection<ListingId> ids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
            return new Dictionary<ListingId, int>();

        var wanted = ids.ToList();

        return await dbContext.Listings
            .AsNoTracking()
            .Where(l => wanted.Contains(l.Id))
            .Select(l => new { l.Id, l.Details.Capacity })
            .ToDictionaryAsync(l => l.Id, l => l.Capacity, cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(Listing listing) => dbContext.Listings.Add(listing);

    /// <summary>
    /// Cada filtro enviado vira mais um <c>AND</c>; os vazios não entram na consulta. Cidade e
    /// bairro comparam sem acento nem maiúsculas: <c>unaccent(city) ILIKE unaccent(@p)</c>.
    /// </summary>
    private static IQueryable<Listing> Filter(IQueryable<Listing> listings, ListingFilters filters)
    {
        if (filters.State is { } state)
            listings = listings.Where(l => l.Address.State == state);

        if (filters.City is { } city)
        {
            var pattern = Literal(city);
            listings = listings.Where(l => EF.Functions.ILike(EF.Functions.Unaccent(l.Address.City), EF.Functions.Unaccent(pattern), LikeEscape));
        }

        if (filters.Neighborhood is { } neighborhood)
        {
            var pattern = Literal(neighborhood);
            listings = listings.Where(l => EF.Functions.ILike(EF.Functions.Unaccent(l.Address.Neighborhood), EF.Functions.Unaccent(pattern), LikeEscape));
        }

        if (filters.Species != AcceptedSpecies.None)
        {
            var species = filters.Species;
            listings = listings.Where(l => (l.Details.AcceptedSpecies & species) == species);
        }

        if (filters.AcceptsSmall)
            listings = listings.Where(l => l.Details.AcceptsSmall);

        if (filters.AcceptsMedium)
            listings = listings.Where(l => l.Details.AcceptsMedium);

        if (filters.AcceptsLarge)
            listings = listings.Where(l => l.Details.AcceptsLarge);

        if (filters.Weekdays != Weekdays.None)
        {
            var weekdays = filters.Weekdays;
            listings = listings.Where(l => (l.Details.AvailableWeekdays & weekdays) == weekdays);
        }

        if (filters.MinNightlyRate is { } minRate)
            listings = listings.Where(l => l.Details.NightlyRate >= minRate);

        if (filters.MaxNightlyRate is { } maxRate)
            listings = listings.Where(l => l.Details.NightlyRate <= maxRate);

        if (filters.MinCapacity is { } minCapacity)
            listings = listings.Where(l => l.Details.Capacity >= minCapacity);

        if (filters.HasYard is { } hasYard)
            listings = listings.Where(l => l.Details.HasYard == hasYard);

        if (filters.HomeType is { } homeType)
            listings = listings.Where(l => l.Details.HomeType == homeType);

        if (filters.HasDogs is { } hasDogs)
            listings = listings.Where(l => l.Details.HasDogs == hasDogs);

        if (filters.HasCats is { } hasCats)
            listings = listings.Where(l => l.Details.HasCats == hasCats);

        if (filters.HasKids is { } hasKids)
            listings = listings.Where(l => l.Details.HasKids == hasKids);

        return listings;
    }

    /// <summary>Só os de dentro do círculo, do mais perto para o mais longe.</summary>
    private static IQueryable<Ranked> WithinRadius(IQueryable<Listing> listings, SearchArea area)
    {
        var originLat = (double)area.Origin.Latitude;
        var originLng = (double)area.Origin.Longitude;
        var originLatRad = originLat * DegreesToRadians;
        var originLngRad = originLng * DegreesToRadians;
        var cosOriginLat = Math.Cos(originLatRad);
        var radiusKm = (double)area.RadiusKm;

        // Caixa delimitadora: grosso, mas barata e indexada. O círculo exato vem no haversine.
        var latDelta = (decimal)(radiusKm / KmPerDegree);
        var lngDelta = (decimal)(radiusKm / (KmPerDegree * Math.Max(cosOriginLat, 0.01)));
        var minLat = area.Origin.Latitude - latDelta;
        var maxLat = area.Origin.Latitude + latDelta;
        var minLng = area.Origin.Longitude - lngDelta;
        var maxLng = area.Origin.Longitude + lngDelta;

        return listings
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
            .Where(x => x.DistanceKm <= radiusKm)
            .OrderBy(x => x.DistanceKm)
            .ThenByDescending(x => x.CreatedAt)
            .Select(x => new Ranked { Id = x.Id, DistanceKm = x.DistanceKm });
    }

    /// <summary>O texto como valor literal no <c>ILIKE</c>: <c>%</c>, <c>_</c> e a barra não viram curinga.</summary>
    private static string Literal(string value) =>
        value.Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);

    /// <summary>Uma linha da página: o id e, na busca por raio, a distância.</summary>
    private sealed class Ranked
    {
        public required ListingId Id { get; init; }

        public double? DistanceKm { get; init; }
    }
}
