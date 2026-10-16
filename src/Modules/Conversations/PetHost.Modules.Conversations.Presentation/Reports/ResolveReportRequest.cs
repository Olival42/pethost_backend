namespace PetHost.Modules.Conversations.Presentation.Reports;

/// <summary>Corpo de <c>PUT /api/v1/conversations/reports/{reportId}/resolution</c>.</summary>
/// <param name="Outcome"><c>upheld</c> (procedente) ou <c>dismissed</c> (improcedente).</param>
/// <param name="Note">Obrigatória, até 1.000 caracteres. Vai para a trilha como motivo.</param>
public sealed record ResolveReportRequest(string? Outcome, string? Note);
