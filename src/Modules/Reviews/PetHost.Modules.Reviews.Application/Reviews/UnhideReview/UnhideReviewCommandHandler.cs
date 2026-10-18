using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Application.Reviews.UnhideReview;

/// <summary>
/// A avaliação volta às listas e à média. Idempotente (já visível: nada muda nem vai para a trilha).
/// A denúncia continua procedente. Inexistente ou apagada: <c>404 REVIEW_NOT_FOUND</c>.
/// </summary>
public sealed class UnhideReviewCommandHandler(
    IReviewRepository reviewRepository,
    IReviewsUnitOfWork unitOfWork,
    ReviewResponseBuilder responseBuilder,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<UnhideReviewCommand, ReviewResponse>
{
    public async Task<Result<ReviewResponse>> HandleAsync(UnhideReviewCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var id = new ReviewId(command.ReviewId);
        var review = await reviewRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (review is null)
            return Result<ReviewResponse>.Failure(ReviewsErrors.NotFound(id));

        if (review.Unhide(timeProvider.GetUtcNow()))
        {
            var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
                return Result<ReviewResponse>.FromFailure(saved);

            await ReviewTrail.RecordModerationAsync(auditTrail, AuditActions.ReviewUnhidden, review, reason: null, cancellationToken).ConfigureAwait(false);
        }

        var response = await responseBuilder
            .BuildAsync(ReviewResponseBuilder.ToRow(review), ReviewView.Admin, cancellationToken)
            .ConfigureAwait(false);

        return Result<ReviewResponse>.Success(response);
    }
}
