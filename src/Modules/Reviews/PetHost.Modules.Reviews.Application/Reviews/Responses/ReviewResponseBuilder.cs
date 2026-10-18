using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Application.Reviews.Responses;

/// <summary>Quem lê a avaliação: decide o que entra na resposta.</summary>
public enum ReviewView
{
    /// <summary>Qualquer pessoa: sem a reserva nem a moderação.</summary>
    Public,

    /// <summary>O autor: com a reserva, o prazo de edição, a publicação e a moderação.</summary>
    Author,

    /// <summary>O admin: tudo.</summary>
    Admin,
}

/// <summary>
/// Monta <see cref="ReviewResponse"/> a partir das linhas, com os autores e os nomes dos pets de
/// todas numa consulta por módulo (sem N+1).
/// </summary>
public sealed class ReviewResponseBuilder(IReviewParties reviewParties)
{
    public async Task<IReadOnlyList<ReviewResponse>> BuildAsync(
        IReadOnlyList<ReviewRow> rows,
        ReviewView view,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
            return [];

        var ownerAuthors = await InfoAsync(ReviewParty.Owner, rows, cancellationToken).ConfigureAwait(false);
        var hostAuthors = await InfoAsync(ReviewParty.Host, rows, cancellationToken).ConfigureAwait(false);

        var petIds = rows.SelectMany(r => r.Pets).Select(p => p.PetId).Distinct().ToList();
        var petNames = petIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await reviewParties.GetPetNamesAsync(petIds, cancellationToken).ConfigureAwait(false);

        return [.. rows.Select(row =>
        {
            var authors = row.AuthorSide == ReviewParty.Owner ? ownerAuthors : hostAuthors;
            return ToResponse(row, authors.GetValueOrDefault(row.AuthorId), petNames, view);
        })];
    }

    public async Task<ReviewResponse> BuildAsync(ReviewRow row, ReviewView view, CancellationToken cancellationToken)
    {
        var items = await BuildAsync([row], view, cancellationToken).ConfigureAwait(false);
        return items[0];
    }

    /// <summary>A linha de uma avaliação que o handler acabou de gravar (sem reler o banco).</summary>
    public static ReviewRow ToRow(Review review)
    {
        ArgumentNullException.ThrowIfNull(review);

        return new ReviewRow(
            review.Id,
            review.BookingId,
            review.AuthorSide,
            review.OwnerId,
            review.HostId,
            review.ListingId,
            review.Rating,
            review.Comment,
            [.. review.PetRatings.Select(p => new PetRatingRow(p.PetId, p.Rating, p.Comment))],
            review.ReplyText,
            review.RepliedAt,
            review.StayEndedAt,
            review.CreatedAt,
            review.UpdatedAt,
            review.HiddenAt,
            review.HiddenReason,
            review.PublishedAt);
    }

    /// <summary>Média com 1 casa (ou nula, sem avaliação) e contagem.</summary>
    public static RatingSummaryResponse ToSummary(RatingRow? rating) =>
        rating is null
            ? new RatingSummaryResponse(null, 0)
            : new RatingSummaryResponse(Math.Round(rating.Average, 1, MidpointRounding.AwayFromZero), rating.Count);

    private async Task<IReadOnlyDictionary<Guid, ReviewPartyInfo>> InfoAsync(
        ReviewParty side,
        IReadOnlyList<ReviewRow> rows,
        CancellationToken cancellationToken)
    {
        var ids = rows.Where(r => r.AuthorSide == side).Select(r => r.AuthorId).Distinct().ToList();

        return ids.Count == 0
            ? new Dictionary<Guid, ReviewPartyInfo>()
            : await reviewParties.GetInfoAsync(side, ids, cancellationToken).ConfigureAwait(false);
    }

    private static ReviewResponse ToResponse(
        ReviewRow row,
        ReviewPartyInfo? author,
        IReadOnlyDictionary<Guid, string> petNames,
        ReviewView view)
    {
        var privileged = view != ReviewView.Public;
        var byHost = row.AuthorSide == ReviewParty.Host;

        return new ReviewResponse(
            row.Id.Value,
            ReviewPartyValues.ToWire(row.AuthorSide),
            row.HostId,
            row.ListingId,
            byHost ? row.OwnerId : null,
            row.Rating,
            row.Comment,
            author is null ? null : new ReviewAuthorSummary(author.Name, author.AvatarUrl),
            byHost ? [.. row.Pets.Select(p => new ReviewPetResponse(p.PetId, petNames.GetValueOrDefault(p.PetId), p.Rating, p.Comment))] : null,
            row.ReplyText is { } text && row.RepliedAt is { } repliedAt ? new ReviewReplyResponse(text, repliedAt) : null,
            row.StayEndedAt,
            row.CreatedAt,
            row.UpdatedAt,
            privileged ? row.BookingId : null,
            privileged ? row.StayEndedAt.AddDays(Review.WindowDays) : null,
            privileged && row.HiddenAt is { } hiddenAt ? new ReviewModerationResponse(hiddenAt, row.HiddenReason) : null,
            privileged ? row.PublishedAt : null);
    }
}
