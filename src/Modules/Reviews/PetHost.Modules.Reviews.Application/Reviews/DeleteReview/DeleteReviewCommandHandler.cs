using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Application.Reviews.DeleteReview;

/// <summary>
/// O autor apaga a avaliação (soft delete), a qualquer momento — inclusive com o perfil inativo ou
/// suspenso, ou com a avaliação oculta: tirar o que escreveu é direito dele. Avaliação de outra
/// pessoa, já apagada ou inexistente: <c>404 REVIEW_NOT_FOUND</c>. A reserva não pode ser avaliada
/// de novo por esse lado.
/// </summary>
public sealed class DeleteReviewCommandHandler(
    IReviewRepository reviewRepository,
    IReviewsUnitOfWork unitOfWork,
    IReviewParties reviewParties,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<DeleteReviewCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeleteReviewCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var reviewer = await ReviewAccess
            .ResolveAsync(reviewParties, command.UserId, command.Role, requireAvailable: false, cancellationToken)
            .ConfigureAwait(false);

        if (reviewer.IsFailure)
            return Result<Unit>.FromFailure(reviewer);

        var id = new ReviewId(command.ReviewId);
        var review = await reviewRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (review is null || !review.IsWrittenBy(reviewer.Value!.Side, reviewer.Value.ProfileId))
            return Result<Unit>.Failure(ReviewsErrors.NotFound(id));

        review.Delete(timeProvider.GetUtcNow());

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<Unit>.FromFailure(saved);

        await ReviewTrail.RecordDeletedAsync(auditTrail, review, cancellationToken).ConfigureAwait(false);

        return Result<Unit>.Success(Unit.Value);
    }
}
