namespace PetHost.Modules.Reviews.Application.Reviews.Responses;

/// <summary>
/// Uma avaliação. O que é só do autor (ou do admin) — <see cref="BookingId"/>,
/// <see cref="EditableUntil"/> e <see cref="Moderation"/> — fica nulo e some do JSON na leitura de
/// outras pessoas.
/// </summary>
/// <param name="AuthorSide">Quem escreveu: <c>owner</c> (o tutor, sobre o anfitrião) ou <c>host</c> (o anfitrião, sobre o tutor e os pets).</param>
/// <param name="HostId">Anfitrião da estadia (<c>host.hosts.id</c>).</param>
/// <param name="ListingId">Cantinho da estadia (<c>listing.listings.id</c>).</param>
/// <param name="OwnerId">Tutor avaliado. Só quando <c>authorSide</c> é <c>host</c>; o tutor autor não é exposto.</param>
/// <param name="Rating">Nota inteira de 1 a 5.</param>
/// <param name="Comment">Comentário. Nulo quando o autor só deu a nota.</param>
/// <param name="Author">Nome curto e foto de quem escreveu. Nulo se a conta sumiu.</param>
/// <param name="Pets">Notas dos pets (só na avaliação do anfitrião).</param>
/// <param name="Reply">Resposta pública do avaliado.</param>
/// <param name="StayEndedAt">Quando a estadia acabou (check-out).</param>
/// <param name="EditableUntil">Até quando o autor pode editar (7 dias depois do check-out).</param>
/// <param name="Moderation">Se o admin ocultou, quando e por quê. Só o autor e o admin veem.</param>
/// <param name="PublishedAt">
/// Quando os outros passam (ou passaram) a ler a avaliação: às cegas, no fim do prazo ou quando o
/// outro lado também avalia. Só o autor e o admin veem.
/// </param>
public sealed record ReviewResponse(
    Guid Id,
    string AuthorSide,
    Guid HostId,
    Guid ListingId,
    Guid? OwnerId,
    int Rating,
    string? Comment,
    ReviewAuthorSummary? Author,
    IReadOnlyList<ReviewPetResponse>? Pets,
    ReviewReplyResponse? Reply,
    DateTimeOffset StayEndedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid? BookingId = null,
    DateTimeOffset? EditableUntil = null,
    ReviewModerationResponse? Moderation = null,
    DateTimeOffset? PublishedAt = null);

/// <summary>Quem escreveu, como aparece: nome curto e foto.</summary>
public sealed record ReviewAuthorSummary(string Name, string? AvatarUrl);

/// <summary>A nota de um pet. <c>name</c> some se o pet não existe mais.</summary>
public sealed record ReviewPetResponse(Guid PetId, string? Name, int Rating, string? Comment);

/// <summary>A resposta pública do avaliado.</summary>
public sealed record ReviewReplyResponse(string Text, DateTimeOffset RepliedAt);

/// <summary>A avaliação foi ocultada pelo admin.</summary>
public sealed record ReviewModerationResponse(DateTimeOffset HiddenAt, string? Reason);

/// <summary>Nota média (1 casa) e contagem.</summary>
public sealed record RatingSummaryResponse(decimal? Average, int Count);
