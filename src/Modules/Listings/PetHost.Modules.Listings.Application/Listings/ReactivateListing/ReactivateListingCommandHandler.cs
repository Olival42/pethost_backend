using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Application.Listings.ReactivateListing;

/// <summary>
/// Reativa o cantinho. Só o dono; idempotente — reativar um ativo não grava nada. Anfitrião
/// inativo ou suspenso não reativa: <c>403 LISTING_HOST_DEACTIVATED</c>.
/// </summary>
public sealed class ReactivateListingCommandHandler(
    IListingRepository listingRepository,
    IListingsUnitOfWork unitOfWork,
    IListingHosts listingHosts,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<ReactivateListingCommand, ListingResponse>
{
    public async Task<Result<ListingResponse>> HandleAsync(ReactivateListingCommand command, CancellationToken cancellationToken)
    {
        var owned = await ListingAccess
            .LoadOwnedAsync(listingRepository, listingHosts, command.UserId, command.ListingId, cancellationToken)
            .ConfigureAwait(false);

        if (owned.IsFailure)
            return Result<ListingResponse>.FromFailure(owned);

        var (listing, host) = owned.Value!;

        if (!listing.IsActive)
        {
            if (!host.IsAvailable)
                return Result<ListingResponse>.Failure(ListingsErrors.HostDeactivated);

            listing.Reactivate(timeProvider.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await auditTrail
                .RecordAsync(new AuditRecord(AuditActions.ListingReactivated, AuditTargets.Listing, listing.Id.Value), cancellationToken)
                .ConfigureAwait(false);
        }

        return Result<ListingResponse>.Success(
            await ListingResponse.LoadAsync(listing, isOwnerView: true, listingHosts, cancellationToken).ConfigureAwait(false));
    }
}
