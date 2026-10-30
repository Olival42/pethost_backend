namespace PetHost.Modules.Conversations.Application.Abstractions;

/// <summary>Porta para o cantinho onde o tutor toca em "Puxar conversa" (módulo Listings).</summary>
public interface IConversationListings
{
    /// <summary>
    /// O anfitrião (<c>host.hosts.id</c>) do cantinho <paramref name="listingId"/>, se o cantinho
    /// aparece na busca — mesmo critério da busca e do pedido de reserva: ativo, de anfitrião ativo,
    /// sem suspensão e com os repasses liberados. Senão, <c>null</c>.
    /// </summary>
    Task<Guid?> FindListableHostIdAsync(Guid listingId, CancellationToken cancellationToken);
}
