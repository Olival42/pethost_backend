using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Application.Listings.AvailabilityBlocks;

/// <summary>A agenda do cantinho: os períodos fechados que ainda não acabaram.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c>, <c>host</c> ou <c>admin</c>.</param>
public sealed record ListAvailabilityBlocksQuery(Guid UserId, string Role, Guid ListingId)
    : IQuery<IReadOnlyList<AvailabilityBlockResponse>>;

/// <summary>
/// Quem vê é quem vê o cantinho (<c>GET /listings/{id}</c>): o dono e o admin, com o motivo;
/// qualquer outra conta, só as datas, e só de cantinho ativo de anfitrião disponível — fora isso,
/// <c>404 LISTING_NOT_FOUND</c>. Do mais próximo para o mais distante, sem paginação (são poucos).
/// </summary>
public sealed class ListAvailabilityBlocksQueryHandler(
    IListingRepository listingRepository,
    IAvailabilityBlockRepository blockRepository,
    IListingHosts listingHosts,
    TimeProvider timeProvider) : IQueryHandler<ListAvailabilityBlocksQuery, IReadOnlyList<AvailabilityBlockResponse>>
{
    public async Task<Result<IReadOnlyList<AvailabilityBlockResponse>>> HandleAsync(ListAvailabilityBlocksQuery query, CancellationToken cancellationToken)
    {
        var id = new ListingId(query.ListingId);
        var listing = await listingRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (listing is null)
            return Result<IReadOnlyList<AvailabilityBlockResponse>>.Failure(ListingsErrors.NotFound(id));

        var withReason = query.Role == Roles.Admin || await IsOwnerAsync(listing, query, cancellationToken).ConfigureAwait(false);
        if (!withReason)
        {
            var hosts = await listingHosts.GetSummariesAsync([listing.HostId], cancellationToken).ConfigureAwait(false);
            if (!listing.IsActive || hosts.GetValueOrDefault(listing.HostId) is not { IsAvailable: true, PayoutsEnabled: true })
                return Result<IReadOnlyList<AvailabilityBlockResponse>>.Failure(ListingsErrors.NotFound(id));
        }

        var open = await blockRepository.ListOpenAsync(id, StayWindow.Today(timeProvider.GetUtcNow()), cancellationToken).ConfigureAwait(false);

        return Result<IReadOnlyList<AvailabilityBlockResponse>>.Success([.. open.Select(block => AvailabilityBlockResponse.From(block, withReason))]);
    }

    private async Task<bool> IsOwnerAsync(Listing listing, ListAvailabilityBlocksQuery query, CancellationToken cancellationToken)
    {
        if (query.Role != Roles.Host)
            return false;

        var host = await listingHosts.FindByAccountAsync(query.UserId, cancellationToken).ConfigureAwait(false);

        return host is not null && listing.IsOwnedBy(host.Id);
    }
}
