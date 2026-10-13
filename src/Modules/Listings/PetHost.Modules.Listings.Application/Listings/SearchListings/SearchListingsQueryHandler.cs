using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Application.Listings.SearchListings;

/// <summary>
/// Busca com filtros somados. Por raio (do mais perto ao mais longe) quando vem raio ou
/// coordenada, ou quando não vem nenhum filtro de lugar; a origem é a coordenada enviada ou, sem
/// ela, o endereço da conta (geocodificado; o resultado fica em cache no Redis). Com estado,
/// cidade ou bairro e sem raio/coordenada, não há distância: do mais novo ao mais antigo. Só
/// aparecem cantinhos ativos de anfitrião ativo, sem suspensão e com os repasses liberados pelo
/// Stripe — inclusive os de quem busca.
/// </summary>
/// <remarks>
/// Filtros e paginação inválidos: <c>400</c> com todos os campos. Busca por raio com conta sem
/// endereço: <c>422 LISTING_SEARCH_ADDRESS_REQUIRED</c>; endereço fora do mapa:
/// <c>422 LISTING_SEARCH_ADDRESS_NOT_LOCATED</c>; mapa fora do ar: <c>503 LISTING_GEOCODER_UNAVAILABLE</c>.
/// <para>
/// Com datas (<c>checkIn</c> + <c>checkOut</c>) e quantos pets vão (<c>pets</c>, padrão 1): só
/// cantinhos que recebem em todos os dias do período (como o pedido de reserva) e com vaga para
/// todos os pets em todas as noites, contando as estadias confirmadas e em andamento. Cada item
/// sai com os selos automáticos (<see cref="ListingBadges"/>).
/// </para>
/// </remarks>
public sealed class SearchListingsQueryHandler(
    IListingRepository listingRepository,
    IListingHosts listingHosts,
    IAccountAddresses accountAddresses,
    IGeocoder geocoder,
    IListingBookings listingBookings,
    TimeProvider timeProvider) : IQueryHandler<SearchListingsQuery, PagedResult<ListingSummaryResponse>>
{
    private const string Small = "small";
    private const string Medium = "medium";
    private const string Large = "large";

    private static readonly string[] SizeValues = [Small, Medium, Large];

    public async Task<Result<PagedResult<ListingSummaryResponse>>> HandleAsync(
        SearchListingsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new List<Error>();
        var filters = ReadFilters(query, errors);
        ValidatePaging(query, errors);

        var window = StayWindow.Parse(query.CheckIn, query.CheckOut, StayWindow.Today(timeProvider.GetUtcNow()));
        if (window.IsFailure)
            errors.AddRange(window.Errors!);

        if (query.Pets is < 1 or > ListingDetails.MaxCapacityCompany)
            errors.Add(ListingsErrors.PetsFilterInvalid);

        if (errors.Count > 0)
            return Result<PagedResult<ListingSummaryResponse>>.Failure(errors);

        var pets = query.Pets ?? 1;
        var stay = window.Value;
        filters = WithStay(filters, stay, query.Pets);

        // Endereço de quem busca: origem da busca por raio e base do selo "Vizinho".
        var viewerAddress = await accountAddresses.GetAsync(query.UserId, cancellationToken).ConfigureAwait(false);

        SearchArea? area = null;
        if (IsRadiusSearch(query, filters))
        {
            var origin = await ResolveOriginAsync(query, viewerAddress, cancellationToken).ConfigureAwait(false);
            if (origin.IsFailure)
                return Result<PagedResult<ListingSummaryResponse>>.FromFailure(origin);

            area = new SearchArea(origin.Value!, query.RadiusKm ?? SearchListingsQuery.DefaultRadiusKm);
        }

        var unavailableHosts = await listingHosts.GetUnavailableIdsAsync(cancellationToken).ConfigureAwait(false);
        var full = stay is null ? [] : await FullListingsAsync(stay, pets, cancellationToken).ConfigureAwait(false);

        var (items, total) = await listingRepository
            .SearchAsync(new ListingSearch(area, filters, unavailableHosts, query.Page, query.PageSize, full), cancellationToken)
            .ConfigureAwait(false);

        var hostIds = items.Select(item => item.Listing.HostId).Distinct().ToList();
        IReadOnlyDictionary<Guid, ListingHostSummary> hosts = hostIds.Count == 0
            ? new Dictionary<Guid, ListingHostSummary>()
            : await listingHosts.GetSummariesAsync(hostIds, cancellationToken).ConfigureAwait(false);
        IReadOnlyDictionary<Guid, HostTrackRecord> records = hostIds.Count == 0
            ? new Dictionary<Guid, HostTrackRecord>()
            : await listingBookings.GetTrackRecordsAsync(hostIds, cancellationToken).ConfigureAwait(false);

        return Result<PagedResult<ListingSummaryResponse>>.Success(new PagedResult<ListingSummaryResponse>(
            [.. items.Select(item =>
            {
                var record = records.GetValueOrDefault(item.Listing.HostId) ?? HostTrackRecord.None;
                var badges = ListingBadges.For(item.Listing, viewerAddress, record.CompletedStays, record.AverageResponseTime);

                return ListingSummaryResponse.From(item, hosts.GetValueOrDefault(item.Listing.HostId), badges);
            })],
            query.Page,
            query.PageSize,
            total));
    }

    /// <summary>
    /// Com datas, o cantinho precisa receber em todos os dias do período; com pets, caber todos
    /// (capacidade mínima). Os filtros que o cliente mandou continuam valendo.
    /// </summary>
    private static ListingFilters WithStay(ListingFilters filters, StayWindow? stay, int? pets)
    {
        if (stay is not null)
            filters = filters with { Weekdays = filters.Weekdays | stay.RequiredWeekdays() };

        if (pets is { } count)
            filters = filters with { MinCapacity = Math.Max(filters.MinCapacity ?? 1, count) };

        return filters;
    }

    /// <summary>
    /// Cantinhos sem vaga para <paramref name="pets"/> pets em alguma noite do período: a maior
    /// ocupação de uma noite mais os pets novos passa da capacidade.
    /// </summary>
    private async Task<IReadOnlyCollection<ListingId>> FullListingsAsync(StayWindow stay, int pets, CancellationToken cancellationToken)
    {
        var peaks = await listingBookings.GetPeakOccupancyAsync(stay.CheckIn, stay.CheckOut, cancellationToken).ConfigureAwait(false);
        if (peaks.Count == 0)
            return [];

        var capacities = await listingRepository
            .GetCapacitiesAsync([.. peaks.Keys.Select(id => new ListingId(id))], cancellationToken)
            .ConfigureAwait(false);

        return [.. capacities.Where(pair => peaks[pair.Key.Value] + pets > pair.Value).Select(pair => pair.Key)];
    }

    /// <summary>Raio ou coordenada enviados, ou nenhum filtro de lugar: busca "perto de".</summary>
    private static bool IsRadiusSearch(SearchListingsQuery query, ListingFilters filters) =>
        query.RadiusKm is not null
        || query.Latitude is not null
        || (filters.State is null && filters.City is null && filters.Neighborhood is null);

    private static ListingFilters ReadFilters(SearchListingsQuery query, List<Error> errors)
    {
        if (query.RadiusKm is < ListingsErrors.MinRadiusKm or > ListingsErrors.MaxRadiusKm)
            errors.Add(ListingsErrors.RadiusOutOfRange);

        if ((query.Latitude is null) != (query.Longitude is null))
            errors.Add(ListingsErrors.CoordinatesIncomplete);

        var state = Text(query.State)?.ToUpperInvariant();
        if (state is not null && !ListingAddress.IsState(state))
            errors.Add(ListingsErrors.StateFilterInvalid);

        var city = Text(query.City);
        if (city?.Length > ListingAddress.CityMaxLength)
            errors.Add(ListingsErrors.CityFilterTooLong);

        var neighborhood = Text(query.Neighborhood);
        if (neighborhood?.Length > ListingAddress.NeighborhoodMaxLength)
            errors.Add(ListingsErrors.NeighborhoodFilterTooLong);

        if (!AcceptedSpeciesValues.TryParse(Values(query.Species), out var species))
            errors.Add(ListingsErrors.SpeciesFilterInvalid);

        var sizes = Values(query.Sizes).Select(size => size.ToLowerInvariant()).ToList();
        if (sizes.Except(SizeValues).Any())
            errors.Add(ListingsErrors.SizeFilterInvalid);

        if (!WeekdaysValues.TryParse(Values(query.Weekdays), out var weekdays))
            errors.Add(ListingsErrors.WeekdaysFilterInvalid);

        if (query.MinNightlyRate < 0)
            errors.Add(ListingsErrors.MinNightlyRateFilterInvalid);

        if (query.MaxNightlyRate < 0 || query.MaxNightlyRate < query.MinNightlyRate)
            errors.Add(ListingsErrors.MaxNightlyRateFilterInvalid);

        if (query.MinCapacity is < 1 or > ListingDetails.MaxCapacityCompany)
            errors.Add(ListingsErrors.MinCapacityFilterInvalid);

        HomeType? homeType = null;
        if (Text(query.HomeType) is { } homeTypeText)
        {
            if (HomeTypeValues.TryParse(homeTypeText, out var parsed))
                homeType = parsed;
            else
                errors.Add(ListingsErrors.HomeTypeInvalid);
        }

        return new ListingFilters(
            state,
            city,
            neighborhood,
            species,
            sizes.Contains(Small),
            sizes.Contains(Medium),
            sizes.Contains(Large),
            weekdays,
            query.MinNightlyRate,
            query.MaxNightlyRate,
            query.MinCapacity,
            query.HasYard,
            homeType,
            query.HasDogs,
            query.HasCats,
            query.HasKids);
    }

    private static void ValidatePaging(SearchListingsQuery query, List<Error> errors)
    {
        if (query.Page < 1)
            errors.Add(ListingsErrors.PageInvalid);

        if (query.PageSize is < 1 or > ListingsErrors.MaxPageSize)
            errors.Add(ListingsErrors.PageSizeInvalid);
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Aceita o parâmetro repetido (<c>species=dog&amp;species=cat</c>) ou separado por vírgula (<c>species=dog,cat</c>).</summary>
    private static IEnumerable<string> Values(IReadOnlyList<string>? values) =>
        (values ?? [])
            .SelectMany(value => (value ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

    private async Task<Result<GeoPoint>> ResolveOriginAsync(
        SearchListingsQuery query,
        ListingAddressData? address,
        CancellationToken cancellationToken)
    {
        if (query.Latitude is { } latitude && query.Longitude is { } longitude)
            return GeoPoint.Create(latitude, longitude);

        if (address is null)
            return Result<GeoPoint>.Failure(ListingsErrors.SearchAddressRequired);

        return await ListingLocator
            .LocateAsync(geocoder, address, ListingsErrors.SearchAddressNotLocated, cancellationToken)
            .ConfigureAwait(false);
    }
}
