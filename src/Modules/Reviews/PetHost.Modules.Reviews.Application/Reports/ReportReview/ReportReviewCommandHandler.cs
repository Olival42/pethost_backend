using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reports.Responses;
using PetHost.Modules.Reviews.Application.Reviews;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Application.Reports.ReportReview;

/// <summary>
/// Denuncia a avaliação para o admin. Só quem a vê denuncia: a do tutor sobre o anfitrião, qualquer
/// tutor ou anfitrião; a do anfitrião sobre o tutor, os anfitriões e o tutor avaliado. Senão, ou
/// apagada, já oculta ou ainda não publicada: <c>404 REVIEW_NOT_FOUND</c>. O autor não denuncia a própria (<c>422</c>).
/// Uma denúncia por conta por avaliação: <c>409 REVIEW_REPORT_ALREADY_EXISTS</c>. A moderação recebe
/// um e-mail.
/// </summary>
public sealed class ReportReviewCommandHandler(
    IReviewRepository reviewRepository,
    IReviewReportRepository reportRepository,
    IReviewsUnitOfWork unitOfWork,
    IReviewParties reviewParties,
    IReviewNotifier reviewNotifier,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<ReportReviewCommand, SubmittedReviewReportResponse>
{
    public async Task<Result<SubmittedReviewReportResponse>> HandleAsync(ReportReviewCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var reviewer = await ReviewAccess
            .ResolveAsync(reviewParties, command.UserId, command.Role, requireAvailable: true, cancellationToken)
            .ConfigureAwait(false);

        if (reviewer.IsFailure)
            return Result<SubmittedReviewReportResponse>.FromFailure(reviewer);

        var id = new ReviewId(command.ReviewId);
        var review = await reviewRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (review is null || !CanSee(review, reviewer.Value!, timeProvider.GetUtcNow()))
            return Result<SubmittedReviewReportResponse>.Failure(ReviewsErrors.NotFound(id));

        var report = ReviewReport.Submit(
            review,
            reviewer.Value!.Side,
            reviewer.Value.ProfileId,
            command.UserId,
            ReviewReportReasonValues.FromWire(command.Reason!),
            command.Description,
            timeProvider.GetUtcNow());

        if (report.IsFailure)
            return Result<SubmittedReviewReportResponse>.FromFailure(report);

        reportRepository.Add(report.Value!);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<SubmittedReviewReportResponse>.FromFailure(saved);

        await ReviewTrail.RecordReportedAsync(auditTrail, report.Value!, cancellationToken).ConfigureAwait(false);
        await reviewNotifier.AlertModeratorsAsync(report.Value!.Id, report.Value.Reason, cancellationToken).ConfigureAwait(false);

        return Result<SubmittedReviewReportResponse>.Success(new SubmittedReviewReportResponse(
            report.Value.Id.Value,
            ReviewReportStatusValues.ToWire(report.Value.Status)));
    }

    /// <summary>
    /// Mesma regra da leitura: oculta ou ainda não publicada ninguém vê; a do anfitrião, só anfitriões
    /// e o tutor avaliado.
    /// </summary>
    private static bool CanSee(Review review, Reviewer reviewer, DateTimeOffset now)
    {
        if (review.IsWrittenBy(reviewer.Side, reviewer.ProfileId))
            return true;

        if (review.IsHidden || !review.IsPublished(now))
            return false;

        return review.AuthorSide == ReviewParty.Owner
            || reviewer.Side == ReviewParty.Host
            || review.IsAbout(reviewer.Side, reviewer.ProfileId);
    }
}
