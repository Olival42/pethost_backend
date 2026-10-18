using PetHost.Modules.Reviews.Application.Reports.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Application.Reports.ResolveReviewReport;

/// <summary>O admin analisa a denúncia: procedente (oculta a avaliação) ou improcedente.</summary>
/// <param name="AdminUserId">Vem do access token.</param>
/// <param name="ReportId">Vem da rota.</param>
/// <param name="Outcome"><c>upheld</c> ou <c>dismissed</c>.</param>
/// <param name="Note">Obrigatória, até 1.000 caracteres. Na procedente, é o motivo de ocultar.</param>
public sealed record ResolveReviewReportCommand(
    Guid AdminUserId,
    Guid ReportId,
    string? Outcome,
    string? Note) : ICommand<ReviewReportResponse>;
