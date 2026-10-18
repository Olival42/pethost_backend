using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Kernel.Domain;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Reviews.Domain.Reports;

/// <summary>
/// Denúncia de uma avaliação, para o admin analisar. Espelha a tabela <c>review.review_reports</c>.
/// </summary>
/// <remarks>
/// Denuncia qualquer tutor ou anfitrião que vê a avaliação, menos o autor. Uma denúncia por conta
/// por avaliação. Procedente (<see cref="ReviewReportStatus.Upheld"/>) oculta a avaliação, com a
/// observação do admin como motivo; improcedente só fecha a denúncia.
/// </remarks>
public sealed class ReviewReport : Entity<ReviewReportId>
{
    /// <summary>Tamanho máximo da descrição de quem denunciou e da observação do admin.</summary>
    public const int TextMaxLength = 1000;

    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private ReviewReport()
    {
    }

    private ReviewReport(
        ReviewId reviewId,
        Guid reportedBy,
        ReviewParty reporterSide,
        ReviewReportReason reason,
        string? description,
        DateTimeOffset now)
        : base(ReviewReportId.New())
    {
        ReviewId = reviewId;
        ReportedBy = reportedBy;
        ReporterSide = reporterSide;
        Reason = reason;
        Description = description;
        Status = ReviewReportStatus.Pending;
        CreatedAt = now;
    }

    /// <summary>A avaliação denunciada.</summary>
    public ReviewId ReviewId { get; private set; }

    /// <summary>Conta de quem denunciou (<c>auth.users.id</c>).</summary>
    public Guid ReportedBy { get; private set; }

    /// <summary>O papel de quem denunciou: tutor ou anfitrião.</summary>
    public ReviewParty ReporterSide { get; private set; }

    public ReviewReportReason Reason { get; private set; }

    /// <summary>O que quem denunciou contou. Obrigatória no motivo <c>other</c>.</summary>
    public string? Description { get; private set; }

    public ReviewReportStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Quando o admin analisou. Nulo enquanto pendente.</summary>
    public DateTimeOffset? ResolvedAt { get; private set; }

    /// <summary>Conta do admin que analisou. Nula enquanto pendente.</summary>
    public Guid? ResolvedBy { get; private set; }

    /// <summary>O que o admin anotou. Na procedente, é o motivo de ocultar a avaliação.</summary>
    public string? ResolutionNote { get; private set; }

    /// <summary>A denúncia ainda espera o admin.</summary>
    public bool IsPending => Status == ReviewReportStatus.Pending;

    /// <summary>
    /// A conta <paramref name="reporterUserId"/> (perfil <paramref name="reporterProfileId"/> do lado
    /// <paramref name="reporterSide"/>) denuncia a <paramref name="review"/>. O autor não denuncia a
    /// própria (<c>REVIEW_REPORT_OWN_REVIEW</c>). Os erros da descrição voltam juntos.
    /// </summary>
    public static Result<ReviewReport> Submit(
        Review review,
        ReviewParty reporterSide,
        Guid reporterProfileId,
        Guid reporterUserId,
        ReviewReportReason reason,
        string? description,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(review);

        if (review.IsWrittenBy(reporterSide, reporterProfileId))
            return Result<ReviewReport>.Failure(ReviewsErrors.ReportOwnReview);

        var text = Review.NormalizeText(description);
        var errors = new List<Error>();

        if (text is null && reason == ReviewReportReason.Other)
            errors.Add(ReviewsErrors.DescriptionRequired);

        if (text is { Length: > TextMaxLength })
            errors.Add(ReviewsErrors.DescriptionTooLong);

        if (text is not null && Review.HasForbiddenControlCharacters(text))
            errors.Add(ReviewsErrors.DescriptionInvalidCharacters);

        if (errors.Count > 0)
            return Result<ReviewReport>.Failure(errors);

        return Result<ReviewReport>.Success(new ReviewReport(review.Id, reporterUserId, reporterSide, reason, text, now));
    }

    /// <summary>
    /// O admin <paramref name="adminUserId"/> analisa: <see cref="ReviewReportStatus.Upheld"/> ou
    /// <see cref="ReviewReportStatus.Dismissed"/>, com observação obrigatória. Já analisada:
    /// <c>REVIEW_REPORT_ALREADY_RESOLVED</c>. Ocultar a avaliação na procedente é do caso de uso.
    /// </summary>
    public Result Resolve(ReviewReportStatus outcome, Guid adminUserId, string? note, DateTimeOffset now)
    {
        if (outcome == ReviewReportStatus.Pending)
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A resolution must be upheld or dismissed.");

        if (adminUserId == Guid.Empty)
            throw new ArgumentException("The admin id cannot be empty.", nameof(adminUserId));

        if (!IsPending)
            return Result.Failure(ReviewsErrors.ReportAlreadyResolved);

        var text = Review.NormalizeText(note);
        var errors = new List<Error>();

        if (text is null)
            errors.Add(ReviewsErrors.NoteRequired);

        if (text is { Length: > TextMaxLength })
            errors.Add(ReviewsErrors.NoteTooLong);

        if (text is not null && Review.HasForbiddenControlCharacters(text))
            errors.Add(ReviewsErrors.NoteInvalidCharacters);

        if (errors.Count > 0)
            return Result.Failure(errors);

        Status = outcome;
        ResolvedAt = now;
        ResolvedBy = adminUserId;
        ResolutionNote = text;

        return Result.Success();
    }
}
