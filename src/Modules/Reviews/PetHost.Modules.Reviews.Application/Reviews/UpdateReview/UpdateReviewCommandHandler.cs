using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Application.Reviews.UpdateReview;

/// <summary>
/// O autor edita a nota, o comentário e/ou as notas dos pets, até 7 dias depois do check-out
/// (<c>422 REVIEW_WINDOW_CLOSED</c>). Avaliação de outra pessoa, apagada ou inexistente:
/// <c>404 REVIEW_NOT_FOUND</c>. Oculta pelo admin: <c>422 REVIEW_HIDDEN_BY_MODERATION</c>. Perfil
/// inativo ou suspenso: <c>403 REVIEW_*_DEACTIVATED</c>. Nada mudou: devolve a avaliação sem gravar
/// nem registrar na trilha.
/// </summary>
public sealed class UpdateReviewCommandHandler(
    IReviewRepository reviewRepository,
    IReviewsUnitOfWork unitOfWork,
    IReviewParties reviewParties,
    IReviewBookings reviewBookings,
    ReviewResponseBuilder responseBuilder,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<UpdateReviewCommand, ReviewResponse>
{
    public async Task<Result<ReviewResponse>> HandleAsync(UpdateReviewCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var reviewer = await ReviewAccess
            .ResolveAsync(reviewParties, command.UserId, command.Role, requireAvailable: true, cancellationToken)
            .ConfigureAwait(false);

        if (reviewer.IsFailure)
            return Result<ReviewResponse>.FromFailure(reviewer);

        var id = new ReviewId(command.ReviewId);
        var review = await reviewRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (review is null || !review.IsWrittenBy(reviewer.Value!.Side, reviewer.Value.ProfileId))
            return Result<ReviewResponse>.Failure(ReviewsErrors.NotFound(id));

        // Trocar as notas dos pets confere de novo os pets da reserva.
        IReadOnlyCollection<Guid>? stayPetIds = null;
        if (command.Pets is not null)
        {
            var stay = await reviewBookings.GetStayAsync(review.BookingId, cancellationToken).ConfigureAwait(false);
            stayPetIds = stay?.PetIds ?? [];
        }

        var changed = review.Update(
            command.Rating,
            command.Comment,
            ReviewPetInput.ToDomain(command.Pets),
            stayPetIds,
            timeProvider.GetUtcNow());

        if (changed.IsFailure)
            return Result<ReviewResponse>.FromFailure(changed);

        if (changed.Value!.Count > 0)
        {
            var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (saved.IsFailure)
                return Result<ReviewResponse>.FromFailure(saved);

            await ReviewTrail.RecordUpdatedAsync(auditTrail, review, changed.Value, cancellationToken).ConfigureAwait(false);
        }

        var response = await responseBuilder
            .BuildAsync(ReviewResponseBuilder.ToRow(review), ReviewView.Author, cancellationToken)
            .ConfigureAwait(false);

        return Result<ReviewResponse>.Success(response);
    }
}
