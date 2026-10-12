using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings.Responses;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Listings.Application.Listings.GetListingById;

/// <summary>Um cantinho, com o anfitrião.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c>, <c>host</c> ou <c>admin</c>.</param>
public sealed record GetListingByIdQuery(Guid UserId, string Role, Guid ListingId) : IQuery<ListingResponse>;

/// <summary>
/// Quem vê: o anfitrião dono, sempre (inclusive inativo), com o endereço completo; o admin, todos;
/// qualquer outra conta logada, o cantinho <b>ativo</b> de anfitrião ativo, sem suspensão e com os
/// repasses liberados pelo Stripe, na visão pública (sem rua, número, CEP e coordenada). Fora
/// isso: <c>404</c>, como se não existisse.
/// </summary>
public sealed class GetListingByIdQueryHandler(
    IListingRepository listingRepository,
    IListingHosts listingHosts) : IQueryHandler<GetListingByIdQuery, ListingResponse>
{
    public async Task<Result<ListingResponse>> HandleAsync(GetListingByIdQuery query, CancellationToken cancellationToken)
    {
        var id = new ListingId(query.ListingId);
        var listing = await listingRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (listing is null)
            return Result<ListingResponse>.Failure(ListingsErrors.NotFound(id));

        var hosts = await listingHosts.GetSummariesAsync([listing.HostId], cancellationToken).ConfigureAwait(false);
        var host = hosts.GetValueOrDefault(listing.HostId);

        if (query.Role == Roles.Admin || await IsOwnerAsync(listing, query, cancellationToken).ConfigureAwait(false))
            return Result<ListingResponse>.Success(ListingResponse.ForOwner(listing, host));

        if (!listing.IsActive || host is not { IsAvailable: true, PayoutsEnabled: true })
            return Result<ListingResponse>.Failure(ListingsErrors.NotFound(id));

        return Result<ListingResponse>.Success(ListingResponse.ForPublic(listing, host));
    }

    private async Task<bool> IsOwnerAsync(Listing listing, GetListingByIdQuery query, CancellationToken cancellationToken)
    {
        if (query.Role != Roles.Host)
            return false;

        var host = await listingHosts.FindByAccountAsync(query.UserId, cancellationToken).ConfigureAwait(false);

        return host is not null && listing.IsOwnedBy(host.Id);
    }
}
