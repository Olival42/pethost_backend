namespace PetHost.Modules.Reviews.Presentation.Reports;

/// <summary>Corpo de <c>PUT /api/v1/reviews/reports/{reportId}/resolution</c>.</summary>
/// <param name="Outcome"><c>upheld</c> (procedente: oculta a avaliação) ou <c>dismissed</c> (improcedente).</param>
/// <param name="Note">Obrigatória, até 1.000 caracteres. Na procedente, é o motivo mostrado ao autor e gravado na trilha.</param>
public sealed record ResolveReviewReportRequest(string? Outcome, string? Note);
