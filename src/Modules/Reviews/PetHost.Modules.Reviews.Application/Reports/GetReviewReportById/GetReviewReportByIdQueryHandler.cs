using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reports.Responses;
using PetHost.Modules.Reviews.Application.Reviews;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Application.Reports.GetReviewReportById;

/// <summary>Inexistente: <c>404 REVIEW_REPORT_NOT_FOUND</c>. A leitura vai para a trilha.</summary>
public sealed class GetReviewReportByIdQueryHandler(
    IReviewQueries reviewQueries,
    ReviewReportResponseBuilder responseBuilder,
    IAuditTrail auditTrail) : IQueryHandler<GetReviewReportByIdQuery, ReviewReportResponse>
{
    public async Task<Result<ReviewReportResponse>> HandleAsync(GetReviewReportByIdQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var id = new ReviewReportId(query.ReportId);
        var row = await reviewQueries.GetReportAsync(query.ReportId, cancellationToken).ConfigureAwait(false);
        if (row is null)
            return Result<ReviewReportResponse>.Failure(ReviewsErrors.ReportNotFound(id));

        var response = await responseBuilder.BuildAsync(row, cancellationToken).ConfigureAwait(false);

        await ReviewTrail.RecordReportViewedAsync(auditTrail, id, cancellationToken).ConfigureAwait(false);

        return Result<ReviewReportResponse>.Success(response);
    }
}
