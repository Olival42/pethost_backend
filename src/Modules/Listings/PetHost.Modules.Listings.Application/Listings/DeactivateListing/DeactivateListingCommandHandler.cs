using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Application.Listings.DeactivateListing;

/// <summary>
/// Inativa o cantinho. Só o dono; idempotente — inativar de novo não grava nada. Com reserva em
/// aberto (solicitada, aceita, confirmada ou em andamento): <c>422 LISTING_HAS_OPEN_BOOKING</c> e
/// nada muda — o anfitrião responde, cancela ou faz o check-out antes (mesmo critério do perfil).
/// </summary>
public sealed class DeactivateListingCommandHandler(
    IListingRepository listingRepository,
    IListingsUnitOfWork unitOfWork,
    IListingHosts listingHosts,
    IListingBookings listingBookings,
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
            // Inativo, o cantinho some da busca e não aceita mais pedidos: a reserva ficaria sem cantinho.
            if (await listingBookings.HasOpenBookingAsync(listing.Id.Value, cancellationToken).ConfigureAwait(false))
                return Result<ListingResponse>.Failure(ListingsErrors.HasOpenBooking);

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
