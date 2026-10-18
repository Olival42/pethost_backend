using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Reviews;

namespace PetHost.Modules.Listings.Infrastructure.Reviews;

/// <summary>Adaptador da porta <see cref="IListingReviews"/> sobre o contrato público <see cref="IReviewDirectory"/>.</summary>
internal sealed class ListingReviews(IReviewDirectory reviewDirectory) : IListingReviews
{
    public async Task<IReadOnlyDictionary<Guid, ListingRating>> GetHostRatingsAsync(
        IReadOnlyCollection<Guid> hostIds,
        CancellationToken cancellationToken) =>
        ToRatings(await reviewDirectory.GetHostRatingsAsync(hostIds, cancellationToken).ConfigureAwait(false));

    public async Task<IReadOnlyDictionary<Guid, ListingRating>> GetListingRatingsAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken) =>
        ToRatings(await reviewDirectory.GetListingRatingsAsync(listingIds, cancellationToken).ConfigureAwait(false));

    private static Dictionary<Guid, ListingRating> ToRatings(IReadOnlyDictionary<Guid, RatingSummary> summaries) =>
        summaries.ToDictionary(pair => pair.Key, pair => new ListingRating(pair.Value.Average, pair.Value.Count));
}
