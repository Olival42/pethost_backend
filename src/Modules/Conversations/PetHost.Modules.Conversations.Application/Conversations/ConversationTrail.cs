using System.Globalization;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Modules.Conversations.Domain.Reports;
using PetHost.Shared.Contracts.Audit;

namespace PetHost.Modules.Conversations.Application.Conversations;

/// <summary>
/// Registro na trilha (§15): conversa aberta, cada mensagem enviada, conversa apagada pela
/// retenção e a moderação (bloqueio, denúncia e o que o admin fez com ela). <b>Nunca</b> o texto,
/// a URL da foto nem a descrição da denúncia — só ids, motivo e se tinha foto.
/// </summary>
internal static class ConversationTrail
{
    public static Task RecordStartedAsync(IAuditTrail auditTrail, Conversation conversation, CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(
                AuditActions.ConversationStarted,
                AuditTargets.Conversation,
                conversation.Id.Value,
                Details: new Dictionary<string, string?>
                {
                    ["ownerId"] = conversation.OwnerId.ToString(),
                    ["hostId"] = conversation.HostId.ToString(),
                }),
            cancellationToken);

    public static Task RecordMessageSentAsync(IAuditTrail auditTrail, Message message, CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(
                AuditActions.ConversationMessageSent,
                AuditTargets.Conversation,
                message.ConversationId.Value,
                Details: new Dictionary<string, string?>
                {
                    ["messageId"] = message.Id.Value.ToString(),
                    ["sentBy"] = ConversationPartyValues.ToWire(message.SentBy),
                    ["hasPhoto"] = message.HasPhoto ? "true" : "false",
                    ["bookingId"] = message.BookingId?.ToString(),
                }),
            cancellationToken);

    /// <summary>Sem usuário no request: quem apagou foi a tarefa de retenção.</summary>
    public static Task RecordPurgedAsync(
        IAuditTrail auditTrail,
        Conversation conversation,
        int photos,
        CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(
                AuditActions.ConversationPurged,
                AuditTargets.Conversation,
                conversation.Id.Value,
                Details: new Dictionary<string, string?>
                {
                    ["lastActivityAt"] = conversation.LastActivityAt.ToString("O", CultureInfo.InvariantCulture),
                    ["photos"] = photos.ToString(CultureInfo.InvariantCulture),
                }),
            cancellationToken);

    /// <summary>Quem bloqueou, na trilha, quando foi a moderação (denúncia procedente).</summary>
    public const string ModerationActor = "moderation";

    /// <param name="action"><see cref="AuditActions.ConversationBlocked"/> ou <see cref="AuditActions.ConversationUnblocked"/>.</param>
    /// <param name="by"><c>owner</c>, <c>host</c> ou <see cref="ModerationActor"/>.</param>
    public static Task RecordBlockChangedAsync(
        IAuditTrail auditTrail,
        string action,
        Conversation conversation,
        string by,
        CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(
                action,
                AuditTargets.Conversation,
                conversation.Id.Value,
                Details: new Dictionary<string, string?> { ["by"] = by }),
            cancellationToken);

    public static Task RecordMessageReportedAsync(IAuditTrail auditTrail, MessageReport report, CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(
                AuditActions.ConversationMessageReported,
                AuditTargets.ConversationReport,
                report.Id.Value,
                Details: new Dictionary<string, string?>
                {
                    ["conversationId"] = report.ConversationId.Value.ToString(),
                    ["messageId"] = report.MessageId.Value.ToString(),
                    ["reportedBy"] = ConversationPartyValues.ToWire(report.ReportedBy),
                    ["reason"] = ReportReasonValues.ToWire(report.Reason),
                }),
            cancellationToken);

    /// <summary>A observação do admin vai como motivo da ação (§15: ação do admin tem motivo).</summary>
    public static Task RecordReportResolvedAsync(IAuditTrail auditTrail, MessageReport report, CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(
                AuditActions.ConversationReportResolved,
                AuditTargets.ConversationReport,
                report.Id.Value,
                Reason: report.ResolutionNote,
                Details: new Dictionary<string, string?>
                {
                    ["conversationId"] = report.ConversationId.Value.ToString(),
                    ["outcome"] = ReportStatusValues.ToWire(report.Status),
                }),
            cancellationToken);

    /// <summary>O admin abriu a denúncia: viu a mensagem e o contexto da conversa (LGPD).</summary>
    public static Task RecordReportViewedAsync(IAuditTrail auditTrail, MessageReportId reportId, CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(AuditActions.ConversationReportViewed, AuditTargets.ConversationReport, reportId.Value),
            cancellationToken);

    public static Task RecordReportsListedAsync(
        IAuditTrail auditTrail,
        string? status,
        int page,
        int count,
        CancellationToken cancellationToken) =>
        auditTrail.RecordAsync(
            new AuditRecord(
                AuditActions.ConversationReportsListed,
                AuditTargets.ConversationReport,
                TargetId: null,
                Details: new Dictionary<string, string?>
                {
                    ["status"] = status,
                    ["page"] = page.ToString(CultureInfo.InvariantCulture),
                    ["count"] = count.ToString(CultureInfo.InvariantCulture),
                }),
            cancellationToken);
}
