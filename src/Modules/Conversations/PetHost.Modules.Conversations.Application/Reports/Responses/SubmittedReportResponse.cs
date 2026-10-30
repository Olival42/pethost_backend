namespace PetHost.Modules.Conversations.Application.Reports.Responses;

/// <summary>O que quem denunciou recebe de volta: a denúncia foi registrada e espera o admin.</summary>
/// <param name="Reason">O motivo escolhido (<c>spam</c>, <c>harassment</c>, <c>scam</c>, <c>inappropriate</c>, <c>other</c>).</param>
/// <param name="Status">Sempre <c>pending</c> ao denunciar.</param>
public sealed record SubmittedReportResponse(
    Guid Id,
    Guid ConversationId,
    Guid MessageId,
    string Reason,
    string Status,
    DateTimeOffset CreatedAt);
