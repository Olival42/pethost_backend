using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PetHost.Modules.Reviews.Application.Reports.GetReviewReportById;
using PetHost.Modules.Reviews.Application.Reports.ListReviewReports;
using PetHost.Modules.Reviews.Application.Reports.ResolveReviewReport;
using PetHost.Modules.Reviews.Application.Reports.Responses;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Application.Reviews.UnhideReview;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Reviews.Presentation.Reports;

/// <summary>
/// Moderação das avaliações, só o admin: a fila de denúncias, a análise (procedente oculta a
/// avaliação com a observação como motivo) e voltar a mostrar uma avaliação ocultada. Tudo vai para
/// a trilha.
/// </summary>
[ApiController]
[Route("api/v1/reviews")]
[Authorize(Roles = Roles.Admin)]
public sealed class ReviewModerationController(
    IQueryHandler<ListReviewReportsQuery, PagedResult<ReviewReportResponse>> listReports,
    IQueryHandler<GetReviewReportByIdQuery, ReviewReportResponse> getReport,
    ICommandHandler<ResolveReviewReportCommand, ReviewReportResponse> resolveReport,
    ICommandHandler<UnhideReviewCommand, ReviewResponse> unhideReview) : ControllerBase
{
    /// <summary>Valor de <c>status</c> que lista todas as situações.</summary>
    private const string AllStatuses = "all";

    /// <summary>A fila de denúncias, da mais antiga para a mais nova, com a avaliação denunciada.</summary>
    /// <remarks>
    /// <c>status</c>: <c>pending</c> (padrão), <c>upheld</c>, <c>dismissed</c> ou <c>all</c>. Paginado.
    /// </remarks>
    [HttpGet("reports")]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewReportResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewReportResponse>>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewReportResponse>>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PagedResult<ReviewReportResponse>>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListReportsAsync([FromQuery] ReviewReportsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var status = string.Equals(request.Status, AllStatuses, StringComparison.Ordinal) ? null : request.Status;
        var result = await listReports.HandleAsync(new ListReviewReportsQuery(status, request.Page, request.PageSize), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Uma denúncia, com a avaliação.</summary>
    /// <remarks>Inexistente: 404 <c>REVIEW_REPORT_NOT_FOUND</c>.</remarks>
    [HttpGet("reports/{reportId:guid}")]
    [ProducesResponseType<ApiResponse<ReviewReportResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ReviewReportResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ReviewReportResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<ReviewReportResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReportAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var result = await getReport.HandleAsync(new GetReviewReportByIdQuery(reportId), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Analisa a denúncia: procedente (<c>upheld</c>, oculta a avaliação) ou improcedente (<c>dismissed</c>).</summary>
    /// <remarks>
    /// Corpo: <c>outcome</c> e <c>note</c> (obrigatória: na procedente, é o motivo que o autor vê e
    /// que vai para a trilha). Já analisada: 422 <c>REVIEW_REPORT_ALREADY_RESOLVED</c>. Outro admin
    /// ao mesmo tempo: 409 <c>REVIEW_CONFLICT</c>.
    /// </remarks>
    [HttpPut("reports/{reportId:guid}/resolution")]
    [ProducesResponseType<ApiResponse<ReviewReportResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ReviewReportResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<ReviewReportResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ReviewReportResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<ReviewReportResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<ReviewReportResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<ReviewReportResponse>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ResolveAsync(
        Guid reportId,
        [FromBody] ResolveReviewReportRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await resolveReport.HandleAsync(
            new ResolveReviewReportCommand(User.GetUserId(), reportId, request.Outcome, request.Note),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Volta a mostrar uma avaliação ocultada pela moderação (procedente dado por engano).</summary>
    /// <remarks>
    /// Idempotente. A denúncia continua procedente. Inexistente ou apagada: 404 <c>REVIEW_NOT_FOUND</c>.
    /// </remarks>
    [HttpDelete("{reviewId:guid}/moderation-hide")]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<ReviewResponse>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UnhideAsync(Guid reviewId, CancellationToken cancellationToken)
    {
        var result = await unhideReview.HandleAsync(new UnhideReviewCommand(User.GetUserId(), reviewId), cancellationToken);

        return result.ToActionResult();
    }
}
