namespace PetHost.Modules.Listings.Domain.Listings;

/// <summary>Acesso a <see cref="Listing"/>. A Application só conhece esta interface (§11).</summary>
public interface IListingRepository
{
    /// <summary>Pelo id, com as fotos. Rastreado: quem chama pode alterar e gravar.</summary>
    Task<Listing?> GetByIdAsync(ListingId id, CancellationToken cancellationToken);

    /// <summary>Todos os cantinhos do anfitrião, ativos e inativos, do mais novo para o mais antigo.</summary>
    Task<IReadOnlyList<Listing>> ListByHostAsync(Guid hostId, CancellationToken cancellationToken);

    /// <summary>Quantos cantinhos o anfitrião tem, ativos e inativos.</summary>
    Task<int> CountByHostAsync(Guid hostId, CancellationToken cancellationToken);

    /// <summary>
    /// Cantinhos <b>ativos</b> que atendem a todos os <see cref="ListingSearch.Filters"/>, paginados.
    /// Com <see cref="ListingSearch.Area"/>, só os de dentro do raio, do mais perto para o mais
    /// longe; sem, do mais novo para o mais antigo. Fora os de <see cref="ListingSearch.ExcludedHostIds"/>.
    /// </summary>
    Task<(IReadOnlyList<FoundListing> Items, long TotalCount)> SearchAsync(
        ListingSearch search,
        CancellationToken cancellationToken);

    /// <summary>A capacidade de cada cantinho pedido. Id que não existe não aparece.</summary>
    Task<IReadOnlyDictionary<ListingId, int>> GetCapacitiesAsync(
        IReadOnlyCollection<ListingId> ids,
        CancellationToken cancellationToken);

    void Add(Listing listing);
}
