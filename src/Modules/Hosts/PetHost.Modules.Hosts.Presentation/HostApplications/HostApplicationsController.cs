using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PetHost.Modules.Hosts.Application.HostApplications.ApproveHostApplication;
using PetHost.Modules.Hosts.Application.HostApplications.GetHostApplicationById;
using PetHost.Modules.Hosts.Application.HostApplications.GetHostApplicationDocument;
using PetHost.Modules.Hosts.Application.HostApplications.ListHostApplications;
using PetHost.Modules.Hosts.Application.HostApplications.RejectHostApplication;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Hosts.Presentation.HostApplications;

/// <summary>
/// A análise do admin: a fila de solicitações de anfitrião, os dados e as fotos de cada uma, e
/// a decisão. Tudo só admin; toda leitura e decisão vai para a trilha de auditoria.
/// </summary>
[ApiController]
[Route("api/v1/host-applications")]
[Authorize(Roles = Roles.Admin)]
public sealed class HostApplicationsController(
    IQueryHandler<ListHostApplicationsQuery, IReadOnlyList<HostApplicationReviewResponse>> listApplications,
    IQueryHandler<GetHostApplicationByIdQuery, HostApplicationReviewResponse> getApplication,
    IQueryHandler<GetHostApplicationDocumentQuery, HostApplicationDocumentFile> getDocument,
    ICommandHandler<ApproveHostApplicationCommand, HostApplicationReviewResponse> approveApplication,
    ICommandHandler<RejectHostApplicationCommand, HostApplicationReviewResponse> rejectApplication) : ControllerBase
{
    /// <summary>A fila do admin: solicitações num status (padrão: <c>submitted</c>, as mais antigas primeiro).</summary>
    /// <remarks>Sem paginação (lista administrativa). Status desconhecido: 400 no campo <c>status</c>.</remarks>
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<HostApplicationReviewResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<HostApplicationReviewResponse>>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<HostApplicationReviewResponse>>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<HostApplicationReviewResponse>>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListAsync([FromQuery] string? status, CancellationToken cancellationToken)
    {
        var result = await listApplications.HandleAsync(new ListHostApplicationsQuery(status), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Uma solicitação, com os dados da conta para comparar e a rota de cada foto.</summary>
    [HttpGet("{applicationId:guid}")]
    [ProducesResponseType<ApiResponse<HostApplicationReviewResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<HostApplicationReviewResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<HostApplicationReviewResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<HostApplicationReviewResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var result = await getApplication.HandleAsync(new GetHostApplicationByIdQuery(applicationId), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>A imagem de uma foto da solicitação: <c>front</c>, <c>back</c> ou <c>selfie</c>.</summary>
    /// <remarks>
    /// Devolve a imagem crua (<c>image/jpeg</c>, <c>image/png</c> ou <c>image/webp</c>), sem cache.
    /// Erro vem no envelope de sempre. Foto já apagada pelo prazo de guarda: 404
    /// <c>HOST_APPLICATION_DOCUMENT_NOT_FOUND</c>.
    /// </remarks>
    [HttpGet("{applicationId:guid}/documents/{kind}")]
    [ProducesResponseType<FileStreamResult>(StatusCodes.Status200OK, "image/jpeg", "image/png", "image/webp")]
    [ProducesResponseType<ApiResponse<HostApplicationDocumentFile>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<HostApplicationDocumentFile>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<HostApplicationDocumentFile>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<HostApplicationDocumentFile>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDocumentAsync(Guid applicationId, string kind, CancellationToken cancellationToken)
    {
        var result = await getDocument.HandleAsync(new GetHostApplicationDocumentQuery(applicationId, kind), cancellationToken);
        if (result.IsFailure)
            return result.ToActionResult();

        // Documento de identidade: nenhum cache (navegador, proxy) guarda a imagem.
        Response.Headers.CacheControl = "no-store";

        var file = result.Value!;
        return File(file.Content, file.ContentType);
    }

    /// <summary>Aprova a solicitação: o perfil de anfitrião é criado. Com motivo.</summary>
    /// <remarks>
    /// Só de solicitação em análise (422 <c>HOST_APPLICATION_NOT_UNDER_REVIEW</c>). Dois admins
    /// ao mesmo tempo: o segundo recebe 409 <c>HOST_APPLICATION_CONFLICT</c>. As fotos são
    /// apagadas conforme o prazo de guarda, e a pessoa recebe um e-mail.
    /// </remarks>
    [HttpPost("{applicationId:guid}/approval")]
    [ProducesResponseType<ApiResponse<HostApplicationReviewResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<HostApplicationReviewResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<HostApplicationReviewResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<HostApplicationReviewResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<HostApplicationReviewResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<HostApplicationReviewResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<HostApplicationReviewResponse>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ApproveAsync(
        Guid applicationId,
        [FromBody] ApproveHostApplicationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await approveApplication.HandleAsync(
            new ApproveHostApplicationCommand(User.GetUserId(), applicationId, request.Reason),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Recusa a solicitação, com o motivo que a pessoa vê e a nota interna.</summary>
    /// <remarks>
    /// <c>rejectionReason</c>: <c>document_illegible</c>, <c>document_expired</c>,
    /// <c>selfie_mismatch</c>, <c>data_mismatch</c>, <c>missing_document</c>, <c>other</c> —
    /// a pessoa pode tentar de novo (até 3 vezes) — ou <c>suspected_fraud</c>, que é definitiva.
    /// </remarks>
    [HttpPost("{applicationId:guid}/rejection")]
    [ProducesResponseType<ApiResponse<HostApplicationReviewResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<HostApplicationReviewResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<HostApplicationReviewResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<HostApplicationReviewResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<HostApplicationReviewResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<HostApplicationReviewResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<HostApplicationReviewResponse>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RejectAsync(
        Guid applicationId,
        [FromBody] RejectHostApplicationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await rejectApplication.HandleAsync(
            new RejectHostApplicationCommand(User.GetUserId(), applicationId, request.RejectionReason, request.Reason),
            cancellationToken);

        return result.ToActionResult();
    }
}

/// <summary>Corpo da aprovação: o que o admin conferiu. Vai para a nota e para a trilha.</summary>
public sealed record ApproveHostApplicationRequest(string? Reason);

/// <summary>Corpo da recusa.</summary>
/// <param name="RejectionReason">O motivo que a pessoa vê.</param>
/// <param name="Reason">Nota interna do admin. Vai para a trilha.</param>
public sealed record RejectHostApplicationRequest(string? RejectionReason, string? Reason);
