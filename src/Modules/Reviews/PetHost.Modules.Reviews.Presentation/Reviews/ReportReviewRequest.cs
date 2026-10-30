namespace PetHost.Modules.Reviews.Presentation.Reviews;

/// <summary>Corpo de <c>POST /api/v1/reviews/{reviewId}/report</c>.</summary>
/// <param name="Reason"><c>offensive</c>, <c>false_information</c>, <c>personal_data</c>, <c>spam</c> ou <c>other</c>.</param>
/// <param name="Description">Obrigatória no motivo <c>other</c>; até 1.000 caracteres.</param>
public sealed record ReportReviewRequest(string? Reason, string? Description);
