using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Application.Listings.ReorderListingPhotos;

/// <summary>
/// Reordena as fotos. Só o dono. Id que não é foto do cantinho: <c>404 LISTING_PHOTO_NOT_FOUND</c>.
/// A lista precisa ter todas as fotos, cada uma uma vez: senão <c>400</c> em <c>photoIds</c>.
/// Mesma ordem de agora não grava nada.
/// </summary>
public sealed class ReorderListingPhotosCommandHandler(
    IListingRepository listingRepository,
    IListingsUnitOfWork unitOfWork,
    IListingHosts listingHosts,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<ReorderListingPhotosCommand, ListingResponse>
{
    public async Task<Result<ListingResponse>> HandleAsync(ReorderListingPhotosCommand command, CancellationToken cancellationToken)
    {
        var owned = await ListingAccess
            .LoadOwnedAsync(listingRepository, listingHosts, command.UserId, command.ListingId, cancellationToken)
            .ConfigureAwait(false);

        if (owned.IsFailure)
            return Result<ListingResponse>.FromFailure(owned);

        var listing = owned.Value!.Listing;
        var photoIds = (command.PhotoIds ?? []).Select(id => new ListingPhotoId(id)).ToList();

        var reordered = listing.ReorderPhotos(photoIds, timeProvider.GetUtcNow());
        if (reordered.IsFailure)
            return Result<ListingResponse>.FromFailure(reordered);

        if (reordered.Value)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await ListingTrail.RecordUpdatedAsync(auditTrail, listing, "photos.reordered", cancellationToken).ConfigureAwait(false);
        }

        return Result<ListingResponse>.Success(
            await ListingResponse.LoadAsync(listing, isOwnerView: true, listingHosts, cancellationToken).ConfigureAwait(false));
    }
}
