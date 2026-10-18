using PetHost.Modules.Reviews.Application.Reviews.Responses;

namespace PetHost.Modules.Reviews.Application.Reports.Responses;

/// <summary>Uma denúncia na fila do admin, com a avaliação denunciada.</summary>
/// <param name="ReportedBy">Papel de quem denunciou: <c>owner</c> ou <c>host</c>.</param>
/// <param name="Reason"><c>offensive</c>, <c>false_information</c>, <c>personal_data</c>, <c>spam</c> ou <c>other</c>.</param>
/// <param name="Status"><c>pending</c>, <c>upheld</c> ou <c>dismissed</c>.</param>
/// <param name="Review">A avaliação, na visão do admin. Nula se o autor a apagou.</param>
public sealed record ReviewReportResponse(
    Guid Id,
    Guid ReviewId,
    string ReportedBy,
    string Reason,
    string? Description,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    string? ResolutionNote,
    ReviewResponse? Review);

/// <summary>A denúncia recebida: o id e a situação (<c>pending</c>).</summary>
public sealed record SubmittedReviewReportResponse(Guid Id, string Status);
