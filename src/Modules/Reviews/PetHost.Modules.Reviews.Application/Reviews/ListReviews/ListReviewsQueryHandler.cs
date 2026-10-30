using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Application.Reviews.ListReviews;

/// <summary>
/// As avaliações que os tutores fizeram dos anfitriões, paginadas (§13), da mais nova para a mais
/// antiga; nunca traz as apagadas nem as ocultas. Paginação ou id inválido: <c>400</c> com todos os
/// campos. O autor aparece só pelo primeiro nome e pela foto.
/// </summary>
public sealed class ListReviewsQueryHandler(
    IReviewQueries reviewQueries,
    ReviewResponseBuilder responseBuilder) : IQueryHandler<ListReviewsQuery, PagedResult<ReviewResponse>>
{
    public async Task<Result<PagedResult<ReviewResponse>>> HandleAsync(ListReviewsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = ReviewPaging.Validate(query.Page, query.PageSize);

        if (query.HostId == Guid.Empty)
            errors.Add(ReviewsErrors.HostIdInvalid);

        if (query.ListingId == Guid.Empty)
            errors.Add(ReviewsErrors.ListingIdInvalid);

        if (errors.Count > 0)
            return Result<PagedResult<ReviewResponse>>.Failure(errors);

        var filter = new ReviewFilter(ReviewParty.Owner, query.HostId, query.ListingId);
        var (rows, total) = await reviewQueries.ListAsync(filter, query.Page, query.PageSize, cancellationToken).ConfigureAwait(false);

        var items = await responseBuilder.BuildAsync(rows, ReviewView.Public, cancellationToken).ConfigureAwait(false);

        return Result<PagedResult<ReviewResponse>>.Success(new PagedResult<ReviewResponse>(items, query.Page, query.PageSize, total));
    }
}
