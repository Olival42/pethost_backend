using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Infrastructure.Persistence.Seed;

/// <summary>Cantinho que o seed cria, com a coordenada já conhecida.</summary>
public sealed record ListingSeed(
    ListingDetailsData Details,
    ListingAddressData Address,
    decimal Latitude,
    decimal Longitude);

/// <summary>
/// Cria o cantinho de um anfitrião para os dados de teste. Mesmas regras do cadastro pela API
/// (anfitrião ativo e com repasses liberados, limite de cantinhos, descrição e endereço pelo
/// domínio), menos o mapa: a coordenada vem pronta, então a subida não depende do Nominatim.
/// </summary>
public sealed class ListingSeeder(
    IListingRepository listingRepository,
    IListingsUnitOfWork unitOfWork,
    IListingHosts listingHosts,
    TimeProvider timeProvider)
{
    /// <returns>O id do cantinho criado.</returns>
    public async Task<Result<Guid>> SeedAsync(Guid userId, ListingSeed seed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seed);

        var resolved = await ListingAccess
            .ResolveHostAsync(listingHosts, userId, requireAvailable: true, cancellationToken)
            .ConfigureAwait(false);

        if (resolved.IsFailure)
            return Result<Guid>.FromFailure(resolved);

        var host = resolved.Value!;
        if (!host.PayoutsEnabled)
            return Result<Guid>.Failure(ListingsErrors.HostPayoutsNotEnabled);

        var details = ListingDetails.Create(seed.Details, host.IsCompany);
        var address = ListingAddress.Create(seed.Address);
        var location = GeoPoint.Create(seed.Latitude, seed.Longitude);
        if (details.IsFailure || address.IsFailure || location.IsFailure)
            return Result<Guid>.Failure([.. details.Errors ?? [], .. address.Errors ?? [], .. location.Errors ?? []]);

        var listingHost = new ListingHost(host.Id, host.IsCompany);
        var listingsOfHost = await listingRepository.CountByHostAsync(host.Id, cancellationToken).ConfigureAwait(false);

        var listing = Listing.Create(listingHost, listingsOfHost, details.Value!, address.Value!, location.Value!, timeProvider.GetUtcNow());
        if (listing.IsFailure)
            return Result<Guid>.FromFailure(listing);

        listingRepository.Add(listing.Value!);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<Guid>.Success(listing.Value!.Id.Value);
    }
}
