using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Shared.Contracts.Hosts;
using PetHost.Shared.Contracts.Listings;

namespace PetHost.Modules.Conversations.Infrastructure.Listings;

/// <summary>
/// Adaptador da porta <see cref="IConversationListings"/> sobre os contratos públicos:
/// <see cref="IListingDirectory"/> (o cantinho) e <see cref="IHostDirectory"/> (o anfitrião aparece
/// para os outros?).
/// </summary>
internal sealed class ConversationListings(
    IListingDirectory listingDirectory,
    IHostDirectory hostDirectory) : IConversationListings
{
    public async Task<Guid?> FindListableHostIdAsync(Guid listingId, CancellationToken cancellationToken)
    {
        var listing = await listingDirectory.GetByIdAsync(listingId, cancellationToken).ConfigureAwait(false);
        if (listing is null || !listing.IsActive)
            return null;

        var hosts = await hostDirectory.GetByIdsAsync([listing.HostId], cancellationToken).ConfigureAwait(false);
        var host = hosts.FirstOrDefault(h => h.Id == listing.HostId);

        // Mesmo critério da busca e do pedido de reserva: anfitrião ativo, sem suspensão e com repasses.
        return host is { IsListable: true } ? host.Id : null;
    }
}
