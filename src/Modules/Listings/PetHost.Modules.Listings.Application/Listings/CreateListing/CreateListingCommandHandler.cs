using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Application.Listings.CreateListing;

/// <summary>
/// Liga o cantinho ao anfitrião da conta logada, valida tudo pelo domínio (conforme pessoa
/// física ou empresa), confere o limite e só então consulta o mapa — nenhuma consulta é gasta
/// com pedido que seria recusado.
/// </summary>
/// <remarks>
/// Conta sem anfitrião: <c>404 LISTING_HOST_NOT_FOUND</c>. Anfitrião inativo ou suspenso:
/// <c>403 LISTING_HOST_DEACTIVATED</c>. Stripe ainda sem aprovar o cadastro de recebimento:
/// <c>422 LISTING_HOST_PAYOUTS_NOT_ENABLED</c> (conferido antes de validar o corpo). Pessoa física com cantinho: <c>422 LISTING_LIMIT_REACHED</c>.
/// Endereço fora do mapa: <c>422 LISTING_ADDRESS_NOT_LOCATED</c>.
/// </remarks>
public sealed class CreateListingCommandHandler(
    IListingRepository listingRepository,
    IListingsUnitOfWork unitOfWork,
    IListingHosts listingHosts,
    IGeocoder geocoder,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<CreateListingCommand, ListingResponse>
{
    public async Task<Result<ListingResponse>> HandleAsync(CreateListingCommand command, CancellationToken cancellationToken)
    {
        var resolved = await ListingAccess
            .ResolveHostAsync(listingHosts, command.UserId, requireAvailable: true, cancellationToken)
            .ConfigureAwait(false);

        if (resolved.IsFailure)
            return Result<ListingResponse>.FromFailure(resolved);

        var host = resolved.Value!;

        // Ordem do anfitrião: verificação (admin) → cadastro de recebimento aprovado (Stripe) → cantinho.
        if (!host.PayoutsEnabled)
            return Result<ListingResponse>.Failure(ListingsErrors.HostPayoutsNotEnabled);

        var details = ListingDetails.Create(command.ToDetailsData(), host.IsCompany);
        var address = ListingAddress.Create(command.Address?.ToData());
        if (details.IsFailure || address.IsFailure)
            return Result<ListingResponse>.Failure([.. details.Errors ?? [], .. address.Errors ?? []]);

        var listingHost = new ListingHost(host.Id, host.IsCompany);
        var listingsOfHost = await listingRepository.CountByHostAsync(host.Id, cancellationToken).ConfigureAwait(false);

        var allowed = Listing.EnsureCanCreate(listingHost, listingsOfHost);
        if (allowed.IsFailure)
            return Result<ListingResponse>.FromFailure(allowed);

        var location = await ListingLocator
            .LocateAsync(geocoder, address.Value!.ToData(), ListingsErrors.AddressNotLocated, cancellationToken)
            .ConfigureAwait(false);

        if (location.IsFailure)
            return Result<ListingResponse>.FromFailure(location);

        var listing = Listing.Create(listingHost, listingsOfHost, details.Value!, address.Value!, location.Value!, timeProvider.GetUtcNow());
        if (listing.IsFailure)
            return Result<ListingResponse>.FromFailure(listing);

        listingRepository.Add(listing.Value!);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditTrail
            .RecordAsync(
                new AuditRecord(
                    AuditActions.ListingCreated,
                    AuditTargets.Listing,
                    listing.Value!.Id.Value,
                    Details: new Dictionary<string, string?>
                    {
                        ["hostId"] = host.Id.ToString(),
                        ["city"] = listing.Value.Address.City,
                    }),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<ListingResponse>.Success(
            await ListingResponse.LoadAsync(listing.Value, isOwnerView: true, listingHosts, cancellationToken).ConfigureAwait(false));
    }
}
