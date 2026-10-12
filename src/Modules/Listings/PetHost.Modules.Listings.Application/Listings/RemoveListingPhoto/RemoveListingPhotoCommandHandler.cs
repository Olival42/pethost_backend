using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Application.Listings.RemoveListingPhoto;

/// <summary>
/// Tira a foto do cantinho e apaga a imagem do bucket. Só o dono; foto que não é do cantinho:
/// <c>404 LISTING_PHOTO_NOT_FOUND</c>.
/// </summary>
public sealed class RemoveListingPhotoCommandHandler(
    IListingRepository listingRepository,
    IListingsUnitOfWork unitOfWork,
    IListingHosts listingHosts,
    IImageStorage imageStorage,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<RemoveListingPhotoCommand, ListingResponse>
{
    public async Task<Result<ListingResponse>> HandleAsync(RemoveListingPhotoCommand command, CancellationToken cancellationToken)
    {
        var owned = await ListingAccess
            .LoadOwnedAsync(listingRepository, listingHosts, command.UserId, command.ListingId, cancellationToken)
            .ConfigureAwait(false);

        if (owned.IsFailure)
            return Result<ListingResponse>.FromFailure(owned);

        var listing = owned.Value!.Listing;

        return await ImageReplacement
            .RemoveAsync(imageStorage, ct => RemoveAsync(listing, new ListingPhotoId(command.PhotoId), ct), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<ImageChange<ListingResponse>>> RemoveAsync(Listing listing, ListingPhotoId photoId, CancellationToken cancellationToken)
    {
        var removed = listing.RemovePhoto(photoId, timeProvider.GetUtcNow());
        if (removed.IsFailure)
            return Result<ImageChange<ListingResponse>>.FromFailure(removed);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ListingTrail.RecordUpdatedAsync(auditTrail, listing, "photos.removed", cancellationToken).ConfigureAwait(false);

        var response = await ListingResponse.LoadAsync(listing, isOwnerView: true, listingHosts, cancellationToken).ConfigureAwait(false);

        return Result<ImageChange<ListingResponse>>.Success(new ImageChange<ListingResponse>(response, removed.Value!.Url));
    }
}
