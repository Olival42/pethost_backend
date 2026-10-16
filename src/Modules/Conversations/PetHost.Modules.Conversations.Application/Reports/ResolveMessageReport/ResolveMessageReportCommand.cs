using PetHost.Modules.Conversations.Application.Reports.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Application.Reports.ResolveMessageReport;

/// <summary>O admin analisa a denúncia: procedente ou improcedente, com observação.</summary>
/// <param name="AdminUserId">Vem do access token.</param>
/// <param name="ReportId">Vem da rota.</param>
/// <param name="Outcome"><c>upheld</c> (procedente) ou <c>dismissed</c> (improcedente).</param>
/// <param name="Note">Obrigatória, até 1.000 caracteres. Vai para a trilha como motivo.</param>
public sealed record ResolveMessageReportCommand(
    Guid AdminUserId,
    Guid ReportId,
    string? Outcome,
    string? Note) : ICommand<MessageReportResponse>;
