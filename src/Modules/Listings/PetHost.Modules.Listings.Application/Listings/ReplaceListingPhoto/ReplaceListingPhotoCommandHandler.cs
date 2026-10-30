using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Application.Listings.ReplaceListingPhoto;

/// <summary>
/// Envia a imagem nova, troca a URL da foto e apaga a antiga. A posse e a existência da foto são
/// conferidas <b>antes</b> do envio: foto que não é do cantinho dá <c>404 LISTING_PHOTO_NOT_FOUND</c>
/// e nada vai para o bucket.
/// </summary>
public sealed class ReplaceListingPhotoCommandHandler(
    IListingRepository listingRepository,
    IListingsUnitOfWork unitOfWork,
    IListingHosts listingHosts,
    IImageStorage imageStorage,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<ReplaceListingPhotoCommand, ListingResponse>
{
    public async Task<Result<ListingResponse>> HandleAsync(ReplaceListingPhotoCommand command, CancellationToken cancellationToken)
    {
        var owned = await ListingAccess
            .LoadOwnedAsync(listingRepository, listingHosts, command.UserId, command.ListingId, cancellationToken)
            .ConfigureAwait(false);

        if (owned.IsFailure)
            return Result<ListingResponse>.FromFailure(owned);

        var listing = owned.Value!.Listing;
        var photoId = new ListingPhotoId(command.PhotoId);

        if (!listing.HasPhoto(photoId))
            return Result<ListingResponse>.Failure(ListingsErrors.PhotoNotFound(photoId));

        return await ImageReplacement
            .ReplaceAsync(imageStorage, command.Image, ImageFolders.Listings, (image, ct) => SaveAsync(listing, photoId, image, ct), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<ImageChange<ListingResponse>>> SaveAsync(
        Listing listing,
        ListingPhotoId photoId,
        StoredImage image,
        CancellationToken cancellationToken)
    {
        var previous = listing.ReplacePhoto(photoId, image.Url, image.ContentHash, timeProvider.GetUtcNow());
        if (previous.IsFailure)
            return Result<ImageChange<ListingResponse>>.FromFailure(previous);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ListingTrail.RecordUpdatedAsync(auditTrail, listing, "photos.replaced", cancellationToken).ConfigureAwait(false);

        var response = await ListingResponse.LoadAsync(listing, isOwnerView: true, listingHosts, cancellationToken).ConfigureAwait(false);

        return Result<ImageChange<ListingResponse>>.Success(new ImageChange<ListingResponse>(response, previous.Value));
    }
}
