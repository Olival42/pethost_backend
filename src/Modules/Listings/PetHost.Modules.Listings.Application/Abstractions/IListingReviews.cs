using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Application.Abstractions;

/// <summary>
/// Porta para as notas dos tutores (módulo Reviews). §5: a Application declara a interface; a
/// Infrastructure implementa via <c>Shared.Contracts</c>.
/// </summary>
public interface IListingReviews
{
    /// <summary>Nota de cada anfitrião (todas as filiais). Sem avaliação, não aparece.</summary>
    Task<IReadOnlyDictionary<Guid, ListingRating>> GetHostRatingsAsync(
        IReadOnlyCollection<Guid> hostIds,
        CancellationToken cancellationToken);

    /// <summary>Nota de cada cantinho. Sem avaliação, não aparece.</summary>
    Task<IReadOnlyDictionary<Guid, ListingRating>> GetListingRatingsAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken);
}
