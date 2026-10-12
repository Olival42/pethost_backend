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
    /// Cantinhos <b>ativos</b> a até <see cref="NearbySearch.RadiusKm"/> da origem, do mais perto
    /// para o mais longe, paginados. Fora os de <see cref="NearbySearch.ExcludedHostIds"/>.
    /// </summary>
    Task<(IReadOnlyList<NearbyListing> Items, long TotalCount)> SearchNearbyAsync(
        NearbySearch search,
        CancellationToken cancellationToken);

    void Add(Listing listing);
}
