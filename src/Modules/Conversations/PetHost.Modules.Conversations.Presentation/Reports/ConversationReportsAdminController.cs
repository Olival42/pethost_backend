using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PetHost.Modules.Conversations.Application.Reports.GetMessageReportById;
using PetHost.Modules.Conversations.Application.Reports.ListMessageReports;
using PetHost.Modules.Conversations.Application.Reports.ResolveMessageReport;
using PetHost.Modules.Conversations.Application.Reports.Responses;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Presentation.Reports;

/// <summary>
/// O admin sobre as denúncias de mensagens do chat: a fila, o detalhe com o contexto e a análise.
/// Tudo só admin; ver e analisar vão para a trilha de auditoria (a análise com a observação como
/// motivo). Suspender quem enviou continua em <c>/api/v1/owners/{id}/suspension</c> e
/// <c>/api/v1/hosts/{id}/suspension</c>.
/// </summary>
[ApiController]
[Route("api/v1/conversations/reports")]
[Authorize(Roles = Roles.Admin)]
public sealed class ConversationReportsAdminController(
    IQueryHandler<ListMessageReportsQuery, PagedResult<MessageReportResponse>> listMessageReports,
    IQueryHandler<GetMessageReportByIdQuery, MessageReportResponse> getMessageReportById,
    ICommandHandler<ResolveMessageReportCommand, MessageReportResponse> resolveMessageReport) : ControllerBase
{
    /// <summary>Valor de <c>status</c> que lista todas as situações.</summary>
    private const string AllStatuses = "all";

    /// <summary>A fila de denúncias, da mais antiga para a mais nova, com a mensagem denunciada.</summary>
    /// <remarks>
    /// <c>?status=</c> <c>pending</c> (padrão), <c>upheld</c>, <c>dismissed</c> ou <c>all</c>. Valor
    /// desconhecido: 400 em <c>status</c>. Paginado: <c>page</c> a partir de 1, <c>pageSize</c> até 100.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<MessageReportResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<PagedResult<MessageReportResponse>>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<PagedResult<MessageReportResponse>>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<PagedResult<MessageReportResponse>>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListAsync([FromQuery] MessageReportsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var status = string.Equals(request.Status, AllStatuses, StringComparison.Ordinal) ? null : request.Status;
        var result = await listMessageReports.HandleAsync(
            new ListMessageReportsQuery(status, request.Page, request.PageSize),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Uma denúncia, com a mensagem e até 10 mensagens anteriores a ela (<c>context</c>).</summary>
    /// <remarks>Em <c>reported</c> está quem enviou a mensagem: o id para suspender, se for o caso.</remarks>
    [HttpGet("{reportId:guid}")]
    [ProducesResponseType<ApiResponse<MessageReportResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<MessageReportResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<MessageReportResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<MessageReportResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var result = await getMessageReportById.HandleAsync(new GetMessageReportByIdQuery(reportId), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Analisa a denúncia: procedente (<c>upheld</c>) ou improcedente (<c>dismissed</c>).</summary>
    /// <remarks>
    /// Corpo: <c>outcome</c> e <c>note</c> (obrigatória, até 1.000 caracteres). Já analisada: 422
    /// <c>CONVERSATION_REPORT_ALREADY_RESOLVED</c>. Dois admins ao mesmo tempo: 409
    /// <c>CONVERSATION_CONFLICT</c>. Analisar não suspende ninguém.
    /// </remarks>
    [HttpPut("{reportId:guid}/resolution")]
    [ProducesResponseType<ApiResponse<MessageReportResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<MessageReportResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<MessageReportResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<MessageReportResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<MessageReportResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<MessageReportResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<MessageReportResponse>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ResolveAsync(Guid reportId, [FromBody] ResolveReportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await resolveMessageReport.HandleAsync(
            new ResolveMessageReportCommand(User.GetUserId(), reportId, request.Outcome, request.Note),
            cancellationToken);

        return result.ToActionResult();
    }
}
