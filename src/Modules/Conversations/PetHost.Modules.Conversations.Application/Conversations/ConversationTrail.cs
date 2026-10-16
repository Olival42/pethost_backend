using System.Globalization;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Contracts.Audit;

namespace PetHost.Modules.Conversations.Application.Conversations;

/// <summary>
/// Registro na trilha (§15): conversa aberta, cada mensagem enviada e conversa apagada pela
/// retenção. <b>Nunca</b> o texto nem a URL da foto — só ids e se tinha foto.
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
}
