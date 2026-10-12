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
/// Busca por raio. A origem é a coordenada enviada ou, sem ela, o endereço da conta
/// (geocodificado; o resultado fica em cache no Redis). Só aparecem cantinhos ativos de anfitrião ativo e
/// sem suspensão — inclusive os do próprio anfitrião que busca.
/// </summary>
/// <remarks>
/// Filtros e paginação inválidos: <c>400</c> com todos os campos. Conta sem endereço:
/// <c>422 LISTING_SEARCH_ADDRESS_REQUIRED</c>; endereço fora do mapa:
/// <c>422 LISTING_SEARCH_ADDRESS_NOT_LOCATED</c>; mapa fora do ar: <c>503 LISTING_GEOCODER_UNAVAILABLE</c>.
/// </remarks>
public sealed class SearchListingsQueryHandler(
    IListingRepository listingRepository,
    IListingHosts listingHosts,
    IAccountAddresses accountAddresses,
    IGeocoder geocoder) : IQueryHandler<SearchListingsQuery, PagedResult<ListingSummaryResponse>>
{
    public async Task<Result<PagedResult<ListingSummaryResponse>>> HandleAsync(
        SearchListingsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = Validate(query);
        if (errors.Count > 0)
            return Result<PagedResult<ListingSummaryResponse>>.Failure(errors);

        var origin = await ResolveOriginAsync(query, cancellationToken).ConfigureAwait(false);
        if (origin.IsFailure)
            return Result<PagedResult<ListingSummaryResponse>>.FromFailure(origin);

        var unavailableHosts = await listingHosts.GetUnavailableIdsAsync(cancellationToken).ConfigureAwait(false);

        var (items, total) = await listingRepository
            .SearchNearbyAsync(
                new NearbySearch(origin.Value!, query.RadiusKm, unavailableHosts, query.Page, query.PageSize),
                cancellationToken)
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

    private static List<Error> Validate(SearchListingsQuery query)
    {
        var errors = new List<Error>();

        if (query.RadiusKm is < ListingsErrors.MinRadiusKm or > ListingsErrors.MaxRadiusKm)
            errors.Add(ListingsErrors.RadiusOutOfRange);

        if ((query.Latitude is null) != (query.Longitude is null))
            errors.Add(ListingsErrors.CoordinatesIncomplete);

        if (query.Page < 1)
            errors.Add(ListingsErrors.PageInvalid);

        if (query.PageSize is < 1 or > ListingsErrors.MaxPageSize)
            errors.Add(ListingsErrors.PageSizeInvalid);

        return errors;
    }

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
