using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Application.Reviews.CreateReview;

/// <summary>
/// Um dos lados avalia a estadia (o lado sai do papel do token). Precisa estar ativo e sem
/// suspensão (<c>403 REVIEW_*_DEACTIVATED</c>); a reserva precisa ser dele (senão,
/// <c>404 REVIEW_BOOKING_NOT_FOUND</c>), estar concluída e ter acabado há no máximo 7 dias (regras
/// no agregado). Uma avaliação por lado por reserva: <c>409 REVIEW_ALREADY_EXISTS</c>, inclusive se
/// a anterior foi apagada.
/// </summary>
/// <remarks>
/// <b>Às cegas:</b> a avaliação nasce guardada até o fim do prazo. Se o outro lado já avaliou (e não
/// apagou), as duas são publicadas agora, na mesma transação, e cada avaliado recebe o e-mail. Senão,
/// quem publica e avisa é a tarefa em segundo plano, no fim do prazo. O tutor, o anfitrião, o
/// cantinho e os pets são copiados da reserva, nunca do corpo. Duas avaliações ao mesmo tempo (duas
/// abas) esbarram no índice único: a segunda recebe o 409.
/// </remarks>
public sealed class CreateReviewCommandHandler(
    IReviewRepository reviewRepository,
    IReviewsUnitOfWork unitOfWork,
    IReviewParties reviewParties,
    IReviewBookings reviewBookings,
    IReviewNotifier reviewNotifier,
    ReviewResponseBuilder responseBuilder,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<CreateReviewCommand, ReviewResponse>
{
    public async Task<Result<ReviewResponse>> HandleAsync(CreateReviewCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var reviewer = await ReviewAccess
            .ResolveAsync(reviewParties, command.UserId, command.Role, requireAvailable: true, cancellationToken)
            .ConfigureAwait(false);

        if (reviewer.IsFailure)
            return Result<ReviewResponse>.FromFailure(reviewer);

        var stay = await reviewBookings.GetStayAsync(command.BookingId, cancellationToken).ConfigureAwait(false);
        if (stay is null)
            return Result<ReviewResponse>.Failure(ReviewsErrors.BookingNotFound(command.BookingId));

        var now = timeProvider.GetUtcNow();
        var created = Review.Create(
            stay,
            reviewer.Value!.Side,
            reviewer.Value.ProfileId,
            command.Rating.GetValueOrDefault(),
            command.Comment,
            ReviewPetInput.ToDomain(command.Pets),
            now);

        if (created.IsFailure)
            return Result<ReviewResponse>.FromFailure(created);

        var review = created.Value!;

        if (await reviewRepository.ExistsForBookingAsync(command.BookingId, review.AuthorSide, cancellationToken).ConfigureAwait(false))
            return Result<ReviewResponse>.Failure(ReviewsErrors.AlreadyExists);

        reviewRepository.Add(review);

        // Os dois lados avaliaram: publica as duas agora e já anota o aviso de cada avaliado.
        var counterpart = await reviewRepository
            .FindByBookingAsync(command.BookingId, review.SubjectSide, cancellationToken)
            .ConfigureAwait(false);

        List<Review> published = [];
        if (counterpart is not null)
        {
            review.Publish(now);
            counterpart.Publish(now);
            published = [review, counterpart];
            published.ForEach(r => r.MarkSubjectNotified(now));
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<ReviewResponse>.FromFailure(saved);

        await ReviewTrail.RecordCreatedAsync(auditTrail, review, cancellationToken).ConfigureAwait(false);

        if (published.Count > 0)
            await ReviewNotices.SendAsync(reviewParties, reviewNotifier, published, cancellationToken).ConfigureAwait(false);

        var response = await responseBuilder
            .BuildAsync(ReviewResponseBuilder.ToRow(review), ReviewView.Author, cancellationToken)
            .ConfigureAwait(false);

        return Result<ReviewResponse>.Success(response);
    }
}
