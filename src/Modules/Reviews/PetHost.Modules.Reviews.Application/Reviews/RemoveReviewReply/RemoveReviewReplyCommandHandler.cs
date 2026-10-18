using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Application.Reviews.RemoveReviewReply;

/// <summary>
/// O avaliado apaga a resposta, a qualquer momento (mesmo com o perfil inativo ou suspenso).
/// Sem resposta: idempotente. Quem não é o avaliado: <c>404 REVIEW_NOT_FOUND</c>.
/// </summary>
public sealed class RemoveReviewReplyCommandHandler(
    IReviewRepository reviewRepository,
    IReviewsUnitOfWork unitOfWork,
    IReviewParties reviewParties,
    ReviewResponseBuilder responseBuilder,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<RemoveReviewReplyCommand, ReviewResponse>
{
    public async Task<Result<ReviewResponse>> HandleAsync(RemoveReviewReplyCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var reviewer = await ReviewAccess
            .ResolveAsync(reviewParties, command.UserId, command.Role, requireAvailable: false, cancellationToken)
            .ConfigureAwait(false);

        if (reviewer.IsFailure)
            return Result<ReviewResponse>.FromFailure(reviewer);

        var id = new ReviewId(command.ReviewId);
        var review = await reviewRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (review is null)
            return Result<ReviewResponse>.Failure(ReviewsErrors.NotFound(id));

        var removed = review.RemoveReply(reviewer.Value!.Side, reviewer.Value.ProfileId, timeProvider.GetUtcNow());
        if (removed.IsFailure)
            return Result<ReviewResponse>.FromFailure(removed);

        if (removed.Value)
        {
            var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
                return Result<ReviewResponse>.FromFailure(saved);

            await ReviewTrail.RecordReplyAsync(auditTrail, AuditActions.ReviewReplyRemoved, review, cancellationToken).ConfigureAwait(false);
        }

        var response = await responseBuilder
            .BuildAsync(ReviewResponseBuilder.ToRow(review), ReviewView.Public, cancellationToken)
            .ConfigureAwait(false);

        return Result<ReviewResponse>.Success(response);
    }
}
