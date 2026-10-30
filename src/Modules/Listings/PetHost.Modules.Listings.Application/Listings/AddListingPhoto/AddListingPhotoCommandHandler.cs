using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Application.Listings.AddListingPhoto;

/// <summary>
/// Envia a imagem ao bucket e põe a foto no fim da fila. A posse e a vaga são conferidas
/// <b>antes</b> do envio: sem vaga, <c>422 LISTING_PHOTO_LIMIT_REACHED</c> e nada vai para o
/// bucket. Imagem que o cantinho já tem: <c>409 LISTING_PHOTO_ALREADY_EXISTS</c>.
/// </summary>
public sealed class AddListingPhotoCommandHandler(
    IListingRepository listingRepository,
    IListingsUnitOfWork unitOfWork,
    IListingHosts listingHosts,
    IImageStorage imageStorage,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<AddListingPhotoCommand, ListingResponse>
{
    public async Task<Result<ListingResponse>> HandleAsync(AddListingPhotoCommand command, CancellationToken cancellationToken)
    {
        var owned = await ListingAccess
            .LoadOwnedAsync(listingRepository, listingHosts, command.UserId, command.ListingId, cancellationToken)
            .ConfigureAwait(false);

        if (owned.IsFailure)
            return Result<ListingResponse>.FromFailure(owned);

        var listing = owned.Value!.Listing;

        if (!listing.CanAddPhoto)
            return Result<ListingResponse>.Failure(ListingsErrors.PhotoLimitReached);

        return await ImageReplacement
            .ReplaceAsync(imageStorage, command.Image, ImageFolders.Listings, (image, ct) => SaveAsync(listing, image, ct), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<ImageChange<ListingResponse>>> SaveAsync(Listing listing, StoredImage image, CancellationToken cancellationToken)
    {
        // Imagem repetida só se sabe depois do envio (é o hash dos bytes): a recusa apaga a
        // imagem recém-enviada (ImageReplacement).
        var added = listing.AddPhoto(image.Url, image.ContentHash, timeProvider.GetUtcNow());
        if (added.IsFailure)
            return Result<ImageChange<ListingResponse>>.FromFailure(added);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await ListingTrail.RecordUpdatedAsync(auditTrail, listing, "photos.added", cancellationToken).ConfigureAwait(false);

        var response = await ListingResponse.LoadAsync(listing, isOwnerView: true, listingHosts, cancellationToken).ConfigureAwait(false);

        return Result<ImageChange<ListingResponse>>.Success(new ImageChange<ListingResponse>(response, PreviousUrl: null));
    }
}
