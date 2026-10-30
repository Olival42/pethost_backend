using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Application.Abstractions;

/// <summary>
/// Leitura do módulo (§11): sem rastreamento, projetada nas linhas. Nunca traz as apagadas. "Agora"
/// (para saber o que já foi publicado) vem do <c>TimeProvider</c> da implementação.
/// </summary>
public interface IReviewQueries
{
    /// <summary>Página de avaliações do filtro, da mais nova para a mais antiga, e o total.</summary>
    Task<(IReadOnlyList<ReviewRow> Items, long TotalCount)> ListAsync(
        ReviewFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// A avaliação pelo id (oculta e ainda não publicada vêm; quem chama decide), ou <c>null</c> se
    /// não existe ou foi apagada.
    /// </summary>
    Task<ReviewRow?> GetAsync(Guid reviewId, CancellationToken cancellationToken);

    /// <summary>Avaliações pelos ids (ocultas vêm). Apagada ou inexistente não aparece.</summary>
    Task<IReadOnlyDictionary<Guid, ReviewRow>> GetManyAsync(IReadOnlyCollection<Guid> reviewIds, CancellationToken cancellationToken);

    /// <summary>
    /// Nota média e contagem de cada perfil, cantinho ou pet de <paramref name="ids"/>, só das
    /// avaliações visíveis (publicadas, sem apagadas nem ocultas). Sem avaliação, não aparece.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, RatingRow>> GetRatingsAsync(
        RatingScope scope,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);

    /// <summary>Página das denúncias (todas, ou só de uma situação), da mais antiga para a mais nova.</summary>
    Task<(IReadOnlyList<ReviewReportRow> Items, long TotalCount)> ListReportsAsync(
        ReviewReportStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>A denúncia pelo id, ou <c>null</c>.</summary>
    Task<ReviewReportRow?> GetReportAsync(Guid reportId, CancellationToken cancellationToken);
}

/// <summary>De quem é a nota.</summary>
public enum RatingScope
{
    /// <summary>Anfitrião (o que os tutores escreveram dele, em todas as filiais).</summary>
    Host,

    /// <summary>Cantinho (o que os tutores escreveram das estadias nele).</summary>
    Listing,

    /// <summary>Tutor (o que os anfitriões escreveram dele).</summary>
    Owner,

    /// <summary>Pet (as notas que os anfitriões deram a ele).</summary>
    Pet,
}

/// <summary>Média (2 casas) e contagem.</summary>
public sealed record RatingRow(decimal Average, int Count);

/// <summary>
/// Filtros somados (E) da lista de avaliações. <see cref="AuthorSide"/> é obrigatório: quem
/// escreveu (o tutor, sobre o anfitrião; o anfitrião, sobre o tutor).
/// </summary>
/// <param name="PetId">Com nota para este pet (só na avaliação do anfitrião).</param>
/// <param name="IncludeHidden">Traz também as ocultas pelo admin (só para o autor).</param>
/// <param name="IncludeUnpublished">Traz também as que ainda não foram publicadas (só para o autor).</param>
public sealed record ReviewFilter(
    ReviewParty AuthorSide,
    Guid? HostId = null,
    Guid? ListingId = null,
    Guid? OwnerId = null,
    Guid? PetId = null,
    bool IncludeHidden = false,
    bool IncludeUnpublished = false);

/// <summary>Uma avaliação como a leitura do módulo a projeta (§11).</summary>
public sealed record ReviewRow(
    ReviewId Id,
    Guid BookingId,
    ReviewParty AuthorSide,
    Guid OwnerId,
    Guid HostId,
    Guid ListingId,
    int Rating,
    string? Comment,
    IReadOnlyList<PetRatingRow> Pets,
    string? ReplyText,
    DateTimeOffset? RepliedAt,
    DateTimeOffset StayEndedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? HiddenAt,
    string? HiddenReason,
    DateTimeOffset PublishedAt)
{
    /// <summary>O perfil de quem escreveu.</summary>
    public Guid AuthorId => AuthorSide == ReviewParty.Owner ? OwnerId : HostId;

    /// <summary>O perfil de quem é avaliado.</summary>
    public Guid SubjectId => AuthorSide == ReviewParty.Owner ? HostId : OwnerId;

    public bool IsHidden => HiddenAt is not null;

    /// <summary>Os outros já podem ler a avaliação em <paramref name="now"/>?</summary>
    public bool IsPublished(DateTimeOffset now) => PublishedAt <= now;
}

/// <summary>A nota de um pet na avaliação do anfitrião.</summary>
public sealed record PetRatingRow(Guid PetId, int Rating, string? Comment);

/// <summary>Uma denúncia como a leitura do módulo a projeta.</summary>
public sealed record ReviewReportRow(
    ReviewReportId Id,
    ReviewId ReviewId,
    Guid ReportedBy,
    ReviewParty ReporterSide,
    ReviewReportReason Reason,
    string? Description,
    ReviewReportStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    Guid? ResolvedBy,
    string? ResolutionNote);
