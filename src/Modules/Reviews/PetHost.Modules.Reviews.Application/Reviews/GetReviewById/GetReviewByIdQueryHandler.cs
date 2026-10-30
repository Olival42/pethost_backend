using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Application.Reviews.GetReviewById;

/// <summary>
/// Quem vê: o admin, tudo; o autor, a sua (inclusive oculta, com a moderação e a reserva); a
/// avaliação do tutor sobre o anfitrião, qualquer pessoa, sem login; a do anfitrião sobre o tutor,
/// os anfitriões e o próprio tutor avaliado. Fora isso — apagada, oculta ou ainda não publicada (às
/// cegas) —: <c>404 REVIEW_NOT_FOUND</c>.
/// </summary>
public sealed class GetReviewByIdQueryHandler(
    IReviewQueries reviewQueries,
    IReviewParties reviewParties,
    ReviewResponseBuilder responseBuilder,
    TimeProvider timeProvider) : IQueryHandler<GetReviewByIdQuery, ReviewResponse>
{
    public async Task<Result<ReviewResponse>> HandleAsync(GetReviewByIdQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var notFound = ReviewsErrors.NotFound(new ReviewId(query.ReviewId));

        var row = await reviewQueries.GetAsync(query.ReviewId, cancellationToken).ConfigureAwait(false);
        if (row is null)
            return Result<ReviewResponse>.Failure(notFound);

        var view = await ViewForAsync(row, query, cancellationToken).ConfigureAwait(false);
        if (view is null)
            return Result<ReviewResponse>.Failure(notFound);

        var response = await responseBuilder.BuildAsync(row, view.Value, cancellationToken).ConfigureAwait(false);

        return Result<ReviewResponse>.Success(response);
    }

    /// <summary>O que quem pede vê, ou <c>null</c> se não vê nada (404).</summary>
    private async Task<ReviewView?> ViewForAsync(ReviewRow row, GetReviewByIdQuery query, CancellationToken cancellationToken)
    {
        if (query.Role == Roles.Admin)
            return ReviewView.Admin;

        var viewer = query.UserId is { } userId
            ? await ReviewAccess.ResolveAsync(reviewParties, userId, query.Role, requireAvailable: false, cancellationToken).ConfigureAwait(false)
            : null;

        var profile = viewer is { IsSuccess: true } ? viewer.Value : null;

        if (profile is not null && profile.Side == row.AuthorSide && profile.ProfileId == row.AuthorId)
            return ReviewView.Author;

        if (row.IsHidden || !row.IsPublished(timeProvider.GetUtcNow()))
            return null;

        if (row.AuthorSide == ReviewParty.Owner)
            return ReviewView.Public;

        // A avaliação do tutor (e dos pets) é para os anfitriões decidirem sobre os pedidos e para o próprio tutor.
        var allowed = query.Role == Roles.Host
            || (profile is { Side: ReviewParty.Owner } && profile.ProfileId == row.OwnerId);

        return allowed ? ReviewView.Public : null;
    }
}
