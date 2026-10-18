using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Application.Reviews.ListPetReviews;

/// <summary>
/// Paginada (§13), da estadia mais recente para a mais antiga, só das avaliações visíveis
/// (publicadas, sem apagadas nem ocultas). Pet sem nota (ou que não existe): lista vazia.
/// </summary>
public sealed class ListPetReviewsQueryHandler(
    IReviewQueries reviewQueries,
    IReviewParties reviewParties) : IQueryHandler<ListPetReviewsQuery, PagedResult<PetReviewResponse>>
{
    public async Task<Result<PagedResult<PetReviewResponse>>> HandleAsync(ListPetReviewsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = ReviewPaging.Validate(query.Page, query.PageSize);

        if (query.PetId == Guid.Empty)
            errors.Add(ReviewsErrors.PetIdInvalid);

        if (errors.Count > 0)
            return Result<PagedResult<PetReviewResponse>>.Failure(errors);

        var filter = new ReviewFilter(ReviewParty.Host, PetId: query.PetId);
        var (rows, total) = await reviewQueries.ListAsync(filter, query.Page, query.PageSize, cancellationToken).ConfigureAwait(false);

        var hostIds = rows.Select(r => r.HostId).Distinct().ToList();
        var hosts = hostIds.Count == 0
            ? new Dictionary<Guid, ReviewPartyInfo>()
            : await reviewParties.GetInfoAsync(ReviewParty.Host, hostIds, cancellationToken).ConfigureAwait(false);

        var items = rows
            .Select(row =>
            {
                var pet = row.Pets.First(p => p.PetId == query.PetId);
                var host = hosts.GetValueOrDefault(row.HostId);

                return new PetReviewResponse(
                    row.Id.Value,
                    row.HostId,
                    row.ListingId,
                    host is null ? null : new ReviewPartySummary(host.Name, host.AvatarUrl),
                    pet.Rating,
                    pet.Comment,
                    row.StayEndedAt);
            })
            .ToList();

        return Result<PagedResult<PetReviewResponse>>.Success(new PagedResult<PetReviewResponse>(items, query.Page, query.PageSize, total));
    }
}
