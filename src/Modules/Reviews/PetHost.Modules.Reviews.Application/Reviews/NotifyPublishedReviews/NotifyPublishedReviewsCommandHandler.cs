using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Application.Reviews.NotifyPublishedReviews;

/// <summary>
/// Pega as avaliações publicadas ainda sem aviso, anota o aviso e manda o e-mail a quem pode
/// receber. A anotação é gravada <b>antes</b> de ir para a caixa de saída: se a gravação falhar,
/// ninguém recebe e a próxima rodada tenta de novo — nunca um e-mail repetido.
/// </summary>
public sealed class NotifyPublishedReviewsCommandHandler(
    IReviewRepository reviewRepository,
    IReviewsUnitOfWork unitOfWork,
    IReviewParties reviewParties,
    IReviewNotifier reviewNotifier,
    TimeProvider timeProvider) : ICommandHandler<NotifyPublishedReviewsCommand, NotifyPublishedReviewsResponse>
{
    public async Task<Result<NotifyPublishedReviewsResponse>> HandleAsync(NotifyPublishedReviewsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = timeProvider.GetUtcNow();
        var reviews = await reviewRepository
            .ListPublishedToNotifyAsync(now, command.BatchSize, cancellationToken)
            .ConfigureAwait(false);

        if (reviews.Count == 0)
            return Result<NotifyPublishedReviewsResponse>.Success(new NotifyPublishedReviewsResponse(0));

        foreach (var review in reviews)
            review.MarkSubjectNotified(now);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<NotifyPublishedReviewsResponse>.FromFailure(saved);

        await ReviewNotices.SendAsync(reviewParties, reviewNotifier, reviews, cancellationToken).ConfigureAwait(false);

        return Result<NotifyPublishedReviewsResponse>.Success(new NotifyPublishedReviewsResponse(reviews.Count));
    }
}
