using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reports.Responses;
using PetHost.Modules.Reviews.Application.Reviews;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Application.Reports.ListReviewReports;

/// <summary>
/// Paginada (§13), da mais antiga para a mais nova, cada denúncia com a avaliação. Status ou
/// paginação inválidos: <c>400</c>. A leitura vai para a trilha (ação do admin).
/// </summary>
public sealed class ListReviewReportsQueryHandler(
    IReviewQueries reviewQueries,
    ReviewReportResponseBuilder responseBuilder,
    IAuditTrail auditTrail) : IQueryHandler<ListReviewReportsQuery, PagedResult<ReviewReportResponse>>
{
    public async Task<Result<PagedResult<ReviewReportResponse>>> HandleAsync(ListReviewReportsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = ReviewPaging.Validate(query.Page, query.PageSize);

        ReviewReportStatus? status = null;
        if (query.Status is not null)
        {
            if (ReviewReportStatusValues.TryFromWire(query.Status, out var parsed))
                status = parsed;
            else
                errors.Add(ReviewsErrors.StatusInvalid);
        }

        if (errors.Count > 0)
            return Result<PagedResult<ReviewReportResponse>>.Failure(errors);

        var (rows, total) = await reviewQueries.ListReportsAsync(status, query.Page, query.PageSize, cancellationToken).ConfigureAwait(false);
        var items = await responseBuilder.BuildAsync(rows, cancellationToken).ConfigureAwait(false);

        await ReviewTrail.RecordReportsListedAsync(auditTrail, query.Status, query.Page, items.Count, cancellationToken).ConfigureAwait(false);

        return Result<PagedResult<ReviewReportResponse>>.Success(new PagedResult<ReviewReportResponse>(items, query.Page, query.PageSize, total));
    }
}
