using PetHost.Modules.Reviews.Application.Reports.Responses;
using PetHost.Modules.Reviews.Application.Reviews;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Application.Reports.ResolveReviewReport;

/// <summary>
/// O admin fecha a denúncia. Procedente: a avaliação é ocultada (some das listas e da média), com a
/// observação como motivo, na mesma transação. Improcedente: a avaliação continua. Denúncia
/// inexistente: <c>404 REVIEW_REPORT_NOT_FOUND</c>; já analisada: <c>422 REVIEW_REPORT_ALREADY_RESOLVED</c>;
/// outro admin ao mesmo tempo: <c>409 REVIEW_CONFLICT</c>.
/// </summary>
public sealed class ResolveReviewReportCommandHandler(
    IReviewReportRepository reportRepository,
    IReviewRepository reviewRepository,
    IReviewsUnitOfWork unitOfWork,
    ReviewReportResponseBuilder responseBuilder,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<ResolveReviewReportCommand, ReviewReportResponse>
{
    public async Task<Result<ReviewReportResponse>> HandleAsync(ResolveReviewReportCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var id = new ReviewReportId(command.ReportId);
        var report = await reportRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (report is null)
            return Result<ReviewReportResponse>.Failure(ReviewsErrors.ReportNotFound(id));

        var now = timeProvider.GetUtcNow();
        var outcome = ReviewReportStatusValues.FromWire(command.Outcome!);

        var resolved = report.Resolve(outcome, command.AdminUserId, command.Note, now);
        if (resolved.IsFailure)
            return Result<ReviewReportResponse>.FromFailure(resolved);

        Review? hidden = null;
        if (outcome == ReviewReportStatus.Upheld)
        {
            var review = await reviewRepository.GetByIdAsync(report.ReviewId, cancellationToken).ConfigureAwait(false);
            if (review is not null && review.Hide(command.AdminUserId, report.ResolutionNote!, now))
                hidden = review;
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<ReviewReportResponse>.FromFailure(saved);

        await ReviewTrail.RecordReportResolvedAsync(auditTrail, report, cancellationToken).ConfigureAwait(false);

        if (hidden is not null)
        {
            await ReviewTrail
                .RecordModerationAsync(auditTrail, AuditActions.ReviewHidden, hidden, report.ResolutionNote, cancellationToken)
                .ConfigureAwait(false);
        }

        var response = await responseBuilder.BuildAsync(ReviewReportResponseBuilder.ToRow(report), cancellationToken).ConfigureAwait(false);

        return Result<ReviewReportResponse>.Success(response);
    }
}
