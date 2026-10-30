using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Application.Listings.ListMyListings;

/// <summary>Os cantinhos do anfitrião logado, ativos e inativos.</summary>
/// <param name="UserId">Vem do access token.</param>
public sealed record ListMyListingsQuery(Guid UserId) : IQuery<MyListingsResponse>;

/// <summary>
/// Sem paginação nem filtro: lista "de uma pessoa" (§13) — pessoa física tem um cantinho, e
/// empresa, um por filial. Conta sem anfitrião: <c>404 LISTING_HOST_NOT_FOUND</c>.
/// </summary>
public sealed class ListMyListingsQueryHandler(
    IListingRepository listingRepository,
    IListingHosts listingHosts) : IQueryHandler<ListMyListingsQuery, MyListingsResponse>
{
    public async Task<Result<MyListingsResponse>> HandleAsync(ListMyListingsQuery query, CancellationToken cancellationToken)
    {
        var resolved = await ListingAccess
            .ResolveHostAsync(listingHosts, query.UserId, requireAvailable: false, cancellationToken)
            .ConfigureAwait(false);

        if (resolved.IsFailure)
            return Result<MyListingsResponse>.FromFailure(resolved);

        var hostId = resolved.Value!.Id;
        var listings = await listingRepository.ListByHostAsync(hostId, cancellationToken).ConfigureAwait(false);
        var hosts = await listingHosts.GetSummariesAsync([hostId], cancellationToken).ConfigureAwait(false);

        return Result<MyListingsResponse>.Success(new MyListingsResponse(
            hosts.GetValueOrDefault(hostId),
            [.. listings.Select(listing => ListingResponse.ForOwner(listing, host: null))]));
    }
}
