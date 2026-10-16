using PetHost.Modules.Conversations.Application.Reports.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Application.Reports.ReportMessage;

/// <summary>Quem recebeu uma mensagem a denuncia para o admin analisar.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c> — o lado de quem denuncia.</param>
/// <param name="ConversationId">Vem da rota.</param>
/// <param name="MessageId">Vem da rota.</param>
/// <param name="Reason"><c>spam</c>, <c>harassment</c>, <c>scam</c>, <c>inappropriate</c> ou <c>other</c>.</param>
/// <param name="Description">Opcional, até 1.000 caracteres; obrigatória no motivo <c>other</c>.</param>
public sealed record ReportMessageCommand(
    Guid UserId,
    string Role,
    Guid ConversationId,
    Guid MessageId,
    string? Reason,
    string? Description) : ICommand<SubmittedReportResponse>;
