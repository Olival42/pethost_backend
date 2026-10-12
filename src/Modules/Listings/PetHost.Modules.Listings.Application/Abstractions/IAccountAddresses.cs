using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Application.Abstractions;

/// <summary>
/// Porta para o endereço da conta logada (módulo Auth): é a origem da busca por raio quando o
/// cliente não manda coordenada.
/// </summary>
public interface IAccountAddresses
{
    /// <summary>O endereço da conta, ou <c>null</c> se ela não tem.</summary>
    Task<ListingAddressData?> GetAsync(Guid userId, CancellationToken cancellationToken);
}
