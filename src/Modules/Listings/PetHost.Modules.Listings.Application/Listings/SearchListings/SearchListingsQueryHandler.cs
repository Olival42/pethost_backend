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
/// aparecem cantinhos ativos de anfitrião ativo e sem suspensão — inclusive os de quem busca.
/// </summary>
/// <remarks>
/// Filtros e paginação inválidos: <c>400</c> com todos os campos. Busca por raio com conta sem
/// endereço: <c>422 LISTING_SEARCH_ADDRESS_REQUIRED</c>; endereço fora do mapa:
/// <c>422 LISTING_SEARCH_ADDRESS_NOT_LOCATED</c>; mapa fora do ar: <c>503 LISTING_GEOCODER_UNAVAILABLE</c>.
/// </remarks>
public sealed class SearchListingsQueryHandler(
    IListingRepository listingRepository,
    IListingHosts listingHosts,
    IAccountAddresses accountAddresses,
    IGeocoder geocoder) : IQueryHandler<SearchListingsQuery, PagedResult<ListingSummaryResponse>>
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
        if (errors.Count > 0)
            return Result<PagedResult<ListingSummaryResponse>>.Failure(errors);

        SearchArea? area = null;
        if (IsRadiusSearch(query, filters))
        {
            var origin = await ResolveOriginAsync(query, cancellationToken).ConfigureAwait(false);
            if (origin.IsFailure)
                return Result<PagedResult<ListingSummaryResponse>>.FromFailure(origin);

            area = new SearchArea(origin.Value!, query.RadiusKm ?? SearchListingsQuery.DefaultRadiusKm);
        }

        var unavailableHosts = await listingHosts.GetUnavailableIdsAsync(cancellationToken).ConfigureAwait(false);

        var (items, total) = await listingRepository
            .SearchAsync(new ListingSearch(area, filters, unavailableHosts, query.Page, query.PageSize), cancellationToken)
            .ConfigureAwait(false);

        var hostIds = items.Select(item => item.Listing.HostId).Distinct().ToList();
        IReadOnlyDictionary<Guid, ListingHostSummary> hosts = hostIds.Count == 0
            ? new Dictionary<Guid, ListingHostSummary>()
            : await listingHosts.GetSummariesAsync(hostIds, cancellationToken).ConfigureAwait(false);

        return Result<PagedResult<ListingSummaryResponse>>.Success(new PagedResult<ListingSummaryResponse>(
            [.. items.Select(item => ListingSummaryResponse.From(item, hosts.GetValueOrDefault(item.Listing.HostId)))],
            query.Page,
            query.PageSize,
            total));
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

    private async Task<Result<GeoPoint>> ResolveOriginAsync(SearchListingsQuery query, CancellationToken cancellationToken)
    {
        if (query.Latitude is { } latitude && query.Longitude is { } longitude)
            return GeoPoint.Create(latitude, longitude);

        var address = await accountAddresses.GetAsync(query.UserId, cancellationToken).ConfigureAwait(false);
        if (address is null)
            return Result<GeoPoint>.Failure(ListingsErrors.SearchAddressRequired);

        return await ListingLocator
            .LocateAsync(geocoder, address, ListingsErrors.SearchAddressNotLocated, cancellationToken)
            .ConfigureAwait(false);
    }
}
