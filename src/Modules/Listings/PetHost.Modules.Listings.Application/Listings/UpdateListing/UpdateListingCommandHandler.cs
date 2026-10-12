using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Application.Listings.UpdateListing;

/// <summary>
/// Altera um cantinho do anfitrião logado. O resultado mesclado é validado inteiro pelo
/// domínio. O mapa só é consultado se o endereço mudou. Nada mudou: não grava nem registra.
/// Cantinho de outro anfitrião: <c>404 LISTING_NOT_FOUND</c>. Vale também para cantinho inativo.
/// </summary>
public sealed class UpdateListingCommandHandler(
    IListingRepository listingRepository,
    IListingsUnitOfWork unitOfWork,
    IListingHosts listingHosts,
    IGeocoder geocoder,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<UpdateListingCommand, ListingResponse>
{
    public async Task<Result<ListingResponse>> HandleAsync(UpdateListingCommand command, CancellationToken cancellationToken)
    {
        var owned = await ListingAccess
            .LoadOwnedAsync(listingRepository, listingHosts, command.UserId, command.ListingId, cancellationToken)
            .ConfigureAwait(false);

        if (owned.IsFailure)
            return Result<ListingResponse>.FromFailure(owned);

        var (listing, host) = owned.Value!;
        var now = timeProvider.GetUtcNow();

        var beforeDetails = listing.Details.ToData();
        var beforeAddress = listing.Address.ToData();

        var details = ListingDetails.Create(ListingPatch.ApplyDetails(beforeDetails, command), host.IsCompany);
        var address = ListingAddress.Create(ListingPatch.ApplyAddress(beforeAddress, command.Address?.ToData()));
        if (details.IsFailure || address.IsFailure)
            return Result<ListingResponse>.Failure([.. details.Errors ?? [], .. address.Errors ?? []]);

        var relocated = false;
        if (address.Value! != listing.Address)
        {
            var location = await ListingLocator
                .LocateAsync(geocoder, address.Value!.ToData(), ListingsErrors.AddressNotLocated, cancellationToken)
                .ConfigureAwait(false);

            if (location.IsFailure)
                return Result<ListingResponse>.FromFailure(location);

            relocated = listing.Relocate(address.Value!, location.Value!, now);
        }

        var described = listing.UpdateDetails(details.Value!, now);

        if (relocated || described)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var changed = ListingPatch.ChangedFields(beforeDetails, listing.Details.ToData(), beforeAddress, listing.Address.ToData());
            await ListingTrail.RecordUpdatedAsync(auditTrail, listing, string.Join(",", changed), cancellationToken).ConfigureAwait(false);
        }

        return Result<ListingResponse>.Success(
            await ListingResponse.LoadAsync(listing, isOwnerView: true, listingHosts, cancellationToken).ConfigureAwait(false));
    }
}
