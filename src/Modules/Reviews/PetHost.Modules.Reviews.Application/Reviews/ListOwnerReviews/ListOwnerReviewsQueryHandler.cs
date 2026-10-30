using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Application.Reviews.ListOwnerReviews;

/// <summary>
/// Paginada (§13), da mais nova para a mais antiga, sem apagadas nem ocultas, com as notas dos pets.
/// Tutor sem avaliação (ou que não existe): lista vazia.
/// </summary>
public sealed class ListOwnerReviewsQueryHandler(
    IReviewQueries reviewQueries,
    ReviewResponseBuilder responseBuilder) : IQueryHandler<ListOwnerReviewsQuery, PagedResult<ReviewResponse>>
{
    public async Task<Result<PagedResult<ReviewResponse>>> HandleAsync(ListOwnerReviewsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = ReviewPaging.Validate(query.Page, query.PageSize);

        if (query.OwnerId == Guid.Empty)
            errors.Add(ReviewsErrors.OwnerIdInvalid);

        if (errors.Count > 0)
            return Result<PagedResult<ReviewResponse>>.Failure(errors);

        var filter = new ReviewFilter(ReviewParty.Host, OwnerId: query.OwnerId);
        var (rows, total) = await reviewQueries.ListAsync(filter, query.Page, query.PageSize, cancellationToken).ConfigureAwait(false);

        var items = await responseBuilder.BuildAsync(rows, ReviewView.Public, cancellationToken).ConfigureAwait(false);

        return Result<PagedResult<ReviewResponse>>.Success(new PagedResult<ReviewResponse>(items, query.Page, query.PageSize, total));
    }
}
