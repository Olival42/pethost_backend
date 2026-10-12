using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Application.Listings;

/// <summary>Quem pode mexer em qual cantinho: só o anfitrião dono.</summary>
public static class ListingAccess
{
    /// <summary>
    /// O anfitrião da conta logada. Conta sem perfil de anfitrião: <c>404 LISTING_HOST_NOT_FOUND</c>.
    /// Com <paramref name="requireAvailable"/>, anfitrião inativo ou suspenso dá
    /// <c>403 LISTING_HOST_DEACTIVATED</c>.
    /// </summary>
    public static async Task<Result<HostProfile>> ResolveHostAsync(
        IListingHosts listingHosts,
        Guid userId,
        bool requireAvailable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(listingHosts);

        var host = await listingHosts.FindByAccountAsync(userId, cancellationToken).ConfigureAwait(false);
        if (host is null)
            return Result<HostProfile>.Failure(ListingsErrors.HostNotFound);

        if (requireAvailable && !host.IsAvailable)
            return Result<HostProfile>.Failure(ListingsErrors.HostDeactivated);

        return Result<HostProfile>.Success(host);
    }

    /// <summary>
    /// O cantinho <paramref name="listingId"/>, rastreado, se for do anfitrião da conta logada.
    /// Inexistente e de outro anfitrião respondem igual, <c>404 LISTING_NOT_FOUND</c>: quem não é
    /// dono não descobre que o id existe.
    /// </summary>
    public static async Task<Result<OwnedListing>> LoadOwnedAsync(
        IListingRepository listingRepository,
        IListingHosts listingHosts,
        Guid userId,
        Guid listingId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(listingRepository);
        ArgumentNullException.ThrowIfNull(listingHosts);

        var id = new ListingId(listingId);
        var listing = await listingRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (listing is null)
            return Result<OwnedListing>.Failure(ListingsErrors.NotFound(id));

        var host = await listingHosts.FindByAccountAsync(userId, cancellationToken).ConfigureAwait(false);
        if (host is null || !listing.IsOwnedBy(host.Id))
            return Result<OwnedListing>.Failure(ListingsErrors.NotFound(id));

        return Result<OwnedListing>.Success(new OwnedListing(listing, host));
    }
}

/// <summary>Um cantinho e o anfitrião dono, já conferido.</summary>
public sealed record OwnedListing(Listing Listing, HostProfile Host);
