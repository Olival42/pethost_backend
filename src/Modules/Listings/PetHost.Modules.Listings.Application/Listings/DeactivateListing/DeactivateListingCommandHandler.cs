using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Application.Listings.DeactivateListing;

/// <summary>Inativa o cantinho. Só o dono; idempotente — inativar de novo não grava nada.</summary>
public sealed class DeactivateListingCommandHandler(
    IListingRepository listingRepository,
    IListingsUnitOfWork unitOfWork,
    IListingHosts listingHosts,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<DeactivateListingCommand, ListingResponse>
{
    public async Task<Result<ListingResponse>> HandleAsync(DeactivateListingCommand command, CancellationToken cancellationToken)
    {
        var owned = await ListingAccess
            .LoadOwnedAsync(listingRepository, listingHosts, command.UserId, command.ListingId, cancellationToken)
            .ConfigureAwait(false);

        if (owned.IsFailure)
            return Result<ListingResponse>.FromFailure(owned);

        var listing = owned.Value!.Listing;

        if (listing.IsActive)
        {
            listing.Deactivate(timeProvider.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await auditTrail
                .RecordAsync(new AuditRecord(AuditActions.ListingDeactivated, AuditTargets.Listing, listing.Id.Value), cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<ListingResponse>.Success(
            await ListingResponse.LoadAsync(listing, isOwnerView: true, listingHosts, cancellationToken).ConfigureAwait(false));
    }
}
