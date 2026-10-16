namespace PetHost.Modules.Conversations.Presentation.Conversations;

/// <summary>Corpo de <c>POST /api/v1/conversations/{conversationId}/messages/{messageId}/report</c>.</summary>
/// <param name="Reason"><c>spam</c>, <c>harassment</c>, <c>scam</c>, <c>inappropriate</c> ou <c>other</c>.</param>
/// <param name="Description">Opcional, até 1.000 caracteres; obrigatória no motivo <c>other</c>.</param>
public sealed record ReportMessageRequest(string? Reason, string? Description);
