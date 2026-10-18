using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Application.Reviews.GetRatingSummary;

/// <summary>
/// Só das avaliações visíveis (publicadas, sem apagadas nem ocultas). Sem avaliação: <c>average</c> nulo e
/// <c>count</c> 0. Nenhum ou mais de um id: <c>400</c>.
/// </summary>
public sealed class GetRatingSummaryQueryHandler(IReviewQueries reviewQueries) : IQueryHandler<GetRatingSummaryQuery, RatingSummaryResponse>
{
    public async Task<Result<RatingSummaryResponse>> HandleAsync(GetRatingSummaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var targets = new List<(RatingScope Scope, Guid Id)>();

        if (query.HostId is { } hostId)
            targets.Add((RatingScope.Host, hostId));

        if (query.ListingId is { } listingId)
            targets.Add((RatingScope.Listing, listingId));

        if (query.OwnerId is { } ownerId)
            targets.Add((RatingScope.Owner, ownerId));

        if (query.PetId is { } petId)
            targets.Add((RatingScope.Pet, petId));

        if (targets is not [var target] || target.Id == Guid.Empty)
            return Result<RatingSummaryResponse>.Failure(ReviewsErrors.RatingScopeInvalid);

        var ratings = await reviewQueries.GetRatingsAsync(target.Scope, [target.Id], cancellationToken).ConfigureAwait(false);

        return Result<RatingSummaryResponse>.Success(ReviewResponseBuilder.ToSummary(ratings.GetValueOrDefault(target.Id)));
    }
}
