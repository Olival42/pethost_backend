using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Application.Messages.Responses;

namespace PetHost.Modules.Conversations.Application.Reports.Responses;

/// <summary>Uma denúncia como o admin vê: o motivo, a mensagem denunciada e quem é quem.</summary>
/// <param name="Status"><c>pending</c>, <c>upheld</c> ou <c>dismissed</c>.</param>
/// <param name="Reason"><c>spam</c>, <c>harassment</c>, <c>scam</c>, <c>inappropriate</c> ou <c>other</c>.</param>
/// <param name="Description">O que quem denunciou contou. Omitida se não contou nada.</param>
/// <param name="ReportedBy"><c>owner</c> ou <c>host</c>: o lado que denunciou.</param>
/// <param name="Reporter">Quem denunciou (id do tutor ou do anfitrião). Omitido se a conta sumiu.</param>
/// <param name="Reported">Quem enviou a mensagem — é este o id para suspender, se for o caso.</param>
/// <param name="Message">A mensagem denunciada, no formato da conversa.</param>
/// <param name="Context">Só no detalhe: as mensagens anteriores à denunciada, da mais nova para a mais antiga.</param>
/// <param name="ResolvedBy">Conta do admin que analisou.</param>
public sealed record MessageReportResponse(
    Guid Id,
    Guid ConversationId,
    string Status,
    string Reason,
    string? Description,
    string ReportedBy,
    PartySummary? Reporter,
    PartySummary? Reported,
    MessageResponse Message,
    IReadOnlyList<MessageResponse>? Context,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    Guid? ResolvedBy,
    string? ResolutionNote);
