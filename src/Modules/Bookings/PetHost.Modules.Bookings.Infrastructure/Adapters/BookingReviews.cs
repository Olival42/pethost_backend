using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Shared.Contracts.Reviews;

namespace PetHost.Modules.Bookings.Infrastructure.Adapters;

/// <summary>Adaptador da porta <see cref="IBookingReviews"/> sobre o contrato público <see cref="IReviewDirectory"/>.</summary>
internal sealed class BookingReviews(IReviewDirectory reviewDirectory) : IBookingReviews
{
    public async Task<IReadOnlyDictionary<Guid, OwnerRating>> GetOwnerRatingsAsync(
        IReadOnlyCollection<Guid> ownerIds,
        CancellationToken cancellationToken)
    {
        var ratings = await reviewDirectory.GetOwnerRatingsAsync(ownerIds, cancellationToken).ConfigureAwait(false);

        return ratings.ToDictionary(pair => pair.Key, pair => new OwnerRating(pair.Value.Average, pair.Value.Count));
    }
}
