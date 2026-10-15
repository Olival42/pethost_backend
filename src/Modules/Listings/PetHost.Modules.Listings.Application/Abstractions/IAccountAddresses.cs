using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Application.Abstractions;

/// <summary>
/// Porta para o endereço de quem está logado: é a origem da busca por raio quando o cliente não
/// manda coordenada, e a base do selo "Vizinho".
/// </summary>
public interface IAccountAddresses
{
    /// <summary>
    /// O endereço de quem busca, ou <c>null</c> se não tem. Logado como anfitrião
    /// (<paramref name="role"/> = <c>host</c>) e sendo empresa, é o endereço da empresa (módulo
    /// Hosts); em qualquer outro caso, o endereço da conta (módulo Auth).
    /// </summary>
    Task<ListingAddressData?> GetAsync(Guid userId, string? role, CancellationToken cancellationToken);
}
