using PetHost.Modules.Reviews.Application.Reports;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Application.Reviews.HideReview;

/// <summary>
/// A avaliação some das listas e da média, com o motivo que o autor vê; as denúncias pendentes dela
/// fecham como procedentes, com o mesmo motivo. Idempotente: já oculta, nada muda (o motivo antigo
/// fica). Inexistente ou apagada: <c>404 REVIEW_NOT_FOUND</c>.
/// </summary>
public sealed class HideReviewCommandHandler(
    IReviewRepository reviewRepository,
    IReviewReportRepository reportRepository,
    IReviewsUnitOfWork unitOfWork,
    ReviewResponseBuilder responseBuilder,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<HideReviewCommand, ReviewResponse>
{
    public async Task<Result<ReviewResponse>> HandleAsync(HideReviewCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var id = new ReviewId(command.ReviewId);
        var review = await reviewRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (review is null)
            return Result<ReviewResponse>.Failure(ReviewsErrors.NotFound(id));

        if (!review.IsHidden)
        {
            var reason = Review.NormalizeText(command.Reason)!;
            var moderated = await ReviewModeration
                .HideAsync(reportRepository, review, command.AdminUserId, reason, timeProvider.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);

            var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
                return Result<ReviewResponse>.FromFailure(saved);

            await ReviewTrail.RecordModerationAsync(auditTrail, AuditActions.ReviewHidden, review, reason, cancellationToken).ConfigureAwait(false);

            foreach (var report in moderated.Upheld)
                await ReviewTrail.RecordReportResolvedAsync(auditTrail, report, cancellationToken).ConfigureAwait(false);
        }

        var response = await responseBuilder
            .BuildAsync(ReviewResponseBuilder.ToRow(review), ReviewView.Admin, cancellationToken)
            .ConfigureAwait(false);

        return Result<ReviewResponse>.Success(response);
    }
}
