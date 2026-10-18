using PetHost.Modules.Reviews.Application.Reports.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Application.Reports.ReportReview;

/// <summary>Um tutor ou anfitrião denuncia uma avaliação que viu.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>.</param>
/// <param name="ReviewId">Vem da rota.</param>
/// <param name="Reason"><c>offensive</c>, <c>false_information</c>, <c>personal_data</c>, <c>spam</c> ou <c>other</c>.</param>
/// <param name="Description">Obrigatória no motivo <c>other</c>; até 1.000 caracteres.</param>
public sealed record ReportReviewCommand(
    Guid UserId,
    string Role,
    Guid ReviewId,
    string? Reason,
    string? Description) : ICommand<SubmittedReviewReportResponse>;
