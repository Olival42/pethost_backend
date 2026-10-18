using PetHost.Modules.Reviews.Application.Reports.Responses;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Application.Reports.ListReviewReports;

/// <summary>A fila de denúncias de avaliação do admin.</summary>
/// <param name="Status"><c>pending</c> (padrão), <c>upheld</c>, <c>dismissed</c> ou nulo para todas.</param>
public sealed record ListReviewReportsQuery(string? Status, int Page = 1, int PageSize = 20) : IQuery<PagedResult<ReviewReportResponse>>;
