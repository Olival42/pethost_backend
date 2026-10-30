using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Shared.Contracts.Reviews;

namespace PetHost.Modules.Reviews.Infrastructure.Persistence.Queries;

/// <summary>
/// O Reviews implementa o contrato público <see cref="IReviewDirectory"/>: as notas dos anfitriões
/// e dos cantinhos (o Listings usa na busca) e dos tutores (o Bookings mostra ao anfitrião no
/// pedido), só das avaliações visíveis.
/// </summary>
internal sealed class ReviewDirectory(IReviewQueries reviewQueries) : IReviewDirectory
{
    public Task<IReadOnlyDictionary<Guid, RatingSummary>> GetHostRatingsAsync(
        IReadOnlyCollection<Guid> hostIds,
        CancellationToken cancellationToken) =>
        RatingsAsync(RatingScope.Host, hostIds, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, RatingSummary>> GetListingRatingsAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken) =>
        RatingsAsync(RatingScope.Listing, listingIds, cancellationToken);

    public Task<IReadOnlyDictionary<Guid, RatingSummary>> GetOwnerRatingsAsync(
        IReadOnlyCollection<Guid> ownerIds,
        CancellationToken cancellationToken) =>
        RatingsAsync(RatingScope.Owner, ownerIds, cancellationToken);

    private async Task<IReadOnlyDictionary<Guid, RatingSummary>> RatingsAsync(
        RatingScope scope,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        var ratings = await reviewQueries.GetRatingsAsync(scope, ids, cancellationToken).ConfigureAwait(false);

        return ratings.ToDictionary(pair => pair.Key, pair => new RatingSummary(pair.Value.Average, pair.Value.Count));
    }
}
