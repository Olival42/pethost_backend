using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Application.Reviews.ListMyReviews;

/// <summary>
/// Paginada (§13), da mais nova para a mais antiga. As que escrevi vêm todas (inclusive as que ainda
/// esperam a publicação às cegas), com a reserva, o prazo de edição, a publicação e, se o admin
/// ocultou, a moderação; as que recebi, como os outros as veem (só publicadas, sem as ocultas).
/// Perfil inativo ou suspenso continua vendo. Conta sem o perfil do papel:
/// <c>404 REVIEW_OWNER_NOT_FOUND</c> / <c>REVIEW_HOST_NOT_FOUND</c>.
/// </summary>
public sealed class ListMyReviewsQueryHandler(
    IReviewParties reviewParties,
    IReviewQueries reviewQueries,
    ReviewResponseBuilder responseBuilder) : IQueryHandler<ListMyReviewsQuery, PagedResult<ReviewResponse>>
{
    public async Task<Result<PagedResult<ReviewResponse>>> HandleAsync(ListMyReviewsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = ReviewPaging.Validate(query.Page, query.PageSize);
        if (errors.Count > 0)
            return Result<PagedResult<ReviewResponse>>.Failure(errors);

        var me = await ReviewAccess
            .ResolveAsync(reviewParties, query.UserId, query.Role, requireAvailable: false, cancellationToken)
            .ConfigureAwait(false);

        if (me.IsFailure)
            return Result<PagedResult<ReviewResponse>>.FromFailure(me);

        var side = me.Value!.Side;
        var authorSide = query.Received ? ReviewPartyValues.Other(side) : side;
        var filter = side == ReviewParty.Owner
            ? new ReviewFilter(authorSide, OwnerId: me.Value.ProfileId, IncludeHidden: !query.Received, IncludeUnpublished: !query.Received)
            : new ReviewFilter(authorSide, HostId: me.Value.ProfileId, IncludeHidden: !query.Received, IncludeUnpublished: !query.Received);

        var (rows, total) = await reviewQueries.ListAsync(filter, query.Page, query.PageSize, cancellationToken).ConfigureAwait(false);

        var view = query.Received ? ReviewView.Public : ReviewView.Author;
        var items = await responseBuilder.BuildAsync(rows, view, cancellationToken).ConfigureAwait(false);

        return Result<PagedResult<ReviewResponse>>.Success(new PagedResult<ReviewResponse>(items, query.Page, query.PageSize, total));
    }
}
