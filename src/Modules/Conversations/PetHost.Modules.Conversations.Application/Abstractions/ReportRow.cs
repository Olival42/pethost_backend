using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Reports;

namespace PetHost.Modules.Conversations.Application.Abstractions;

/// <summary>Uma denúncia como a leitura projeta, com a mensagem denunciada e os dois lados da conversa.</summary>
/// <param name="OwnerId">Tutor da conversa (<c>owner.owners.id</c>).</param>
/// <param name="HostId">Anfitrião da conversa (<c>host.hosts.id</c>).</param>
/// <param name="ReportedBy">Quem denunciou; o outro lado é quem enviou a mensagem.</param>
/// <param name="ResolvedBy">Conta do admin que analisou.</param>
public sealed record ReportRow(
    MessageReportId Id,
    ConversationId ConversationId,
    Guid OwnerId,
    Guid HostId,
    MessageRow Message,
    ConversationParty ReportedBy,
    ReportReason Reason,
    string? Description,
    ReportStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    Guid? ResolvedBy,
    string? ResolutionNote);
