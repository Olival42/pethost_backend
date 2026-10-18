using System.Globalization;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Audit;

namespace PetHost.Modules.Reviews.Application.Reviews;

/// <summary>
/// Registro na trilha (§15): avaliação criada, editada, apagada, respondida, denunciada e moderada,
/// e o lembrete enviado. <b>Nunca</b> o comentário, a resposta nem a descrição da denúncia — só ids,
/// notas, motivo e o nome dos campos alterados.
/// </summary>
internal static class ReviewTrail
{
    public static Task RecordCreatedAsync(IAuditTrail auditTrail, Review review, CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(
                AuditActions.ReviewCreated,
                AuditTargets.Review,
                review.Id.Value,
                Details: new Dictionary<string, string?>
                {
                    ["authorSide"] = ReviewPartyValues.ToWire(review.AuthorSide),
                    ["bookingId"] = review.BookingId.ToString(),
                    ["ownerId"] = review.OwnerId.ToString(),
                    ["hostId"] = review.HostId.ToString(),
                    ["listingId"] = review.ListingId.ToString(),
                    ["rating"] = review.Rating.ToString(CultureInfo.InvariantCulture),
                    ["pets"] = review.PetRatings.Count.ToString(CultureInfo.InvariantCulture),
                }),
            cancellationToken);

    public static Task RecordUpdatedAsync(
        IAuditTrail auditTrail,
        Review review,
        IReadOnlyList<string> changed,
        CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(
                AuditActions.ReviewUpdated,
                AuditTargets.Review,
                review.Id.Value,
                Details: new Dictionary<string, string?>
                {
                    ["fields"] = string.Join(",", changed),
                    ["rating"] = review.Rating.ToString(CultureInfo.InvariantCulture),
                }),
            cancellationToken);

    public static Task RecordDeletedAsync(IAuditTrail auditTrail, Review review, CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(
                AuditActions.ReviewDeleted,
                AuditTargets.Review,
                review.Id.Value,
                Details: new Dictionary<string, string?> { ["bookingId"] = review.BookingId.ToString() }),
            cancellationToken);

    /// <param name="action"><see cref="AuditActions.ReviewReplied"/> ou <see cref="AuditActions.ReviewReplyRemoved"/>.</param>
    public static Task RecordReplyAsync(IAuditTrail auditTrail, string action, Review review, CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(
                action,
                AuditTargets.Review,
                review.Id.Value,
                Details: new Dictionary<string, string?> { ["by"] = ReviewPartyValues.ToWire(review.SubjectSide) }),
            cancellationToken);

    public static Task RecordReportedAsync(IAuditTrail auditTrail, ReviewReport report, CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(
                AuditActions.ReviewReported,
                AuditTargets.ReviewReport,
                report.Id.Value,
                Details: new Dictionary<string, string?>
                {
                    ["reviewId"] = report.ReviewId.Value.ToString(),
                    ["reportedBy"] = ReviewPartyValues.ToWire(report.ReporterSide),
                    ["reason"] = ReviewReportReasonValues.ToWire(report.Reason),
                }),
            cancellationToken);

    /// <summary>A observação do admin vai como motivo da ação (§15: ação do admin tem motivo).</summary>
    public static Task RecordReportResolvedAsync(IAuditTrail auditTrail, ReviewReport report, CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(
                AuditActions.ReviewReportResolved,
                AuditTargets.ReviewReport,
                report.Id.Value,
                Reason: report.ResolutionNote,
                Details: new Dictionary<string, string?>
                {
                    ["reviewId"] = report.ReviewId.Value.ToString(),
                    ["outcome"] = ReviewReportStatusValues.ToWire(report.Status),
                }),
            cancellationToken);

    /// <param name="action"><see cref="AuditActions.ReviewHidden"/> ou <see cref="AuditActions.ReviewUnhidden"/>.</param>
    public static Task RecordModerationAsync(
        IAuditTrail auditTrail,
        string action,
        Review review,
        string? reason,
        CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(action, AuditTargets.Review, review.Id.Value, Reason: reason),
            cancellationToken);

    public static Task RecordReportViewedAsync(IAuditTrail auditTrail, ReviewReportId reportId, CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(AuditActions.ReviewReportViewed, AuditTargets.ReviewReport, reportId.Value),
            cancellationToken);

    public static Task RecordReportsListedAsync(
        IAuditTrail auditTrail,
        string? status,
        int page,
        int count,
        CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(
                AuditActions.ReviewReportsListed,
                AuditTargets.ReviewReport,
                TargetId: null,
                Details: new Dictionary<string, string?>
                {
                    ["status"] = status,
                    ["page"] = page.ToString(CultureInfo.InvariantCulture),
                    ["count"] = count.ToString(CultureInfo.InvariantCulture),
                }),
            cancellationToken);

    /// <summary>Sem usuário no request: quem mandou foi a tarefa do lembrete.</summary>
    public static Task RecordReminderSentAsync(
        IAuditTrail auditTrail,
        Guid bookingId,
        ReviewParty side,
        CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(
                AuditActions.ReviewReminderSent,
                AuditTargets.Booking,
                bookingId,
                Details: new Dictionary<string, string?> { ["to"] = ReviewPartyValues.ToWire(side) }),
            cancellationToken);
}
