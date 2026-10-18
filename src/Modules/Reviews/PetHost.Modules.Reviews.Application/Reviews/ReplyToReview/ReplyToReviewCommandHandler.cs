using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Application.Reviews.ReplyToReview;

/// <summary>
/// O avaliado (o anfitrião, na avaliação do tutor; o tutor, na do anfitrião) responde em público.
/// Uma resposta por avaliação, que ele pode trocar ou apagar, sem prazo. Quem não é o avaliado,
/// avaliação apagada ou ainda não publicada (às cegas): <c>404 REVIEW_NOT_FOUND</c>. Oculta: <c>422 REVIEW_HIDDEN_BY_MODERATION</c>.
/// Perfil inativo ou suspenso: <c>403 REVIEW_*_DEACTIVATED</c>.
/// </summary>
public sealed class ReplyToReviewCommandHandler(
    IReviewRepository reviewRepository,
    IReviewsUnitOfWork unitOfWork,
    IReviewParties reviewParties,
    ReviewResponseBuilder responseBuilder,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<ReplyToReviewCommand, ReviewResponse>
{
    public async Task<Result<ReviewResponse>> HandleAsync(ReplyToReviewCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var reviewer = await ReviewAccess
            .ResolveAsync(reviewParties, command.UserId, command.Role, requireAvailable: true, cancellationToken)
            .ConfigureAwait(false);

        if (reviewer.IsFailure)
            return Result<ReviewResponse>.FromFailure(reviewer);

        var id = new ReviewId(command.ReviewId);
        var review = await reviewRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (review is null)
            return Result<ReviewResponse>.Failure(ReviewsErrors.NotFound(id));

        var replied = review.Reply(reviewer.Value!.Side, reviewer.Value.ProfileId, command.Text, timeProvider.GetUtcNow());
        if (replied.IsFailure)
            return Result<ReviewResponse>.FromFailure(replied);

        if (replied.Value)
        {
            var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
                return Result<ReviewResponse>.FromFailure(saved);

            await ReviewTrail.RecordReplyAsync(auditTrail, AuditActions.ReviewReplied, review, cancellationToken).ConfigureAwait(false);
        }

        var response = await responseBuilder
            .BuildAsync(ReviewResponseBuilder.ToRow(review), ReviewView.Public, cancellationToken)
            .ConfigureAwait(false);

        return Result<ReviewResponse>.Success(response);
    }
}
