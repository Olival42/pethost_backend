using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Application.Reviews;

/// <summary>
/// O e-mail "você recebeu uma avaliação", que sai na <b>publicação</b> (às cegas), nunca no envio:
/// antes disso o avaliado nem lê a avaliação. Uma consulta de contatos e uma de nomes por lado.
/// </summary>
internal static class ReviewNotices
{
    /// <summary>
    /// Avisa o avaliado de cada avaliação que pode receber aviso. Quem marca
    /// <see cref="Review.MarkSubjectNotified"/> (e grava) é o caso de uso, antes de chamar isto.
    /// </summary>
    public static async Task SendAsync(
        IReviewParties reviewParties,
        IReviewNotifier reviewNotifier,
        IReadOnlyList<Review> reviews,
        CancellationToken cancellationToken)
    {
        foreach (var group in reviews.GroupBy(r => r.AuthorSide))
        {
            var authorSide = group.Key;
            var subjectSide = ReviewPartyValues.Other(authorSide);
            var items = group.ToList();

            var contacts = await reviewParties
                .GetContactsAsync(subjectSide, [.. items.Select(r => SubjectId(r)).Distinct()], cancellationToken)
                .ConfigureAwait(false);

            var authors = await reviewParties
                .GetInfoAsync(authorSide, [.. items.Select(r => AuthorId(r)).Distinct()], cancellationToken)
                .ConfigureAwait(false);

            foreach (var review in items)
            {
                if (contacts.GetValueOrDefault(SubjectId(review)) is not { IsReachable: true } contact)
                    continue;

                var authorName = authors.GetValueOrDefault(AuthorId(review))?.Name ?? string.Empty;

                await reviewNotifier
                    .NotifyReceivedAsync(contact, authorSide, authorName, review.Rating, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static Guid AuthorId(Review review) => review.AuthorSide == ReviewParty.Owner ? review.OwnerId : review.HostId;

    private static Guid SubjectId(Review review) => review.AuthorSide == ReviewParty.Owner ? review.HostId : review.OwnerId;
}
