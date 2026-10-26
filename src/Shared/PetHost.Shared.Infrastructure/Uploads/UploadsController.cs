using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Infrastructure.RateLimiting;

namespace PetHost.Shared.Infrastructure.Uploads;

/// <summary>
/// Upload direto do navegador ao bucket, com URL assinada: o arquivo não passa pela API nem pelo
/// Next.js. Um só para a API inteira; cada módulo diz o que fazer com o arquivo
/// (<see cref="IUploadPurpose"/>). Contrato completo em <c>docs/upload-direto.md</c>.
/// </summary>
[ApiController]
[Route("api/v1/uploads")]
[Authorize(Roles = $"{Roles.Owner},{Roles.Host}")]
public sealed class UploadsController(UploadService uploads) : ControllerBase
{
    /// <summary>Pede uma URL assinada para enviar um arquivo direto ao bucket.</summary>
    /// <remarks>
    /// Confere a permissão antes de assinar (posse do pet, vaga livre, conversa aberta...), com as
    /// mesmas regras e erros do caso de uso. A URL só aceita o <c>Content-Type</c> e o tamanho
    /// declarados (vão na assinatura). Depois do <c>PUT</c>, confirme em
    /// <c>POST /api/v1/uploads/{uploadId}/confirm</c>.
    /// </remarks>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    [ProducesResponseType<ApiResponse<UploadTicketResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<UploadTicketResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<UploadTicketResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<UploadTicketResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<UploadTicketResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<UploadTicketResponse>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<UploadTicketResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RequestAsync([FromBody] RequestUploadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await uploads.RequestAsync(
            new RequestUploadCommand(
                User.GetUserId(),
                CurrentRole(),
                request.Purpose,
                request.ContentType,
                request.SizeBytes,
                request.ResourceId,
                request.PhotoId,
                request.Kind),
            cancellationToken);

        return result.ToActionResult(StatusCodes.Status201Created);
    }

    /// <summary>Confirma o upload: confere o arquivo que chegou ao bucket e o aplica ao recurso.</summary>
    /// <remarks>
    /// Idempotente: confirmar de novo um upload concluído devolve a mesma resposta, sem duplicar
    /// foto nem mensagem. Corpo opcional; <c>body</c> é a legenda da foto do chat.
    /// </remarks>
    [HttpPost("{uploadId:guid}/confirm")]
    [ProducesResponseType<ApiResponse<UploadConfirmationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<UploadConfirmationResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<UploadConfirmationResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<UploadConfirmationResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<UploadConfirmationResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<UploadConfirmationResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<UploadConfirmationResponse>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ConfirmAsync(
        Guid uploadId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ConfirmUploadRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await uploads.ConfirmAsync(
            new ConfirmUploadCommand(User.GetUserId(), CurrentRole(), uploadId, request?.Body),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Confirms one to three host document slots atomically.</summary>
    [HttpPost("documents/confirm")]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<UploadConfirmationResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ConfirmDocumentsAsync([FromBody] ConfirmDocumentsRequest request, CancellationToken cancellationToken)
    {
        var result = await uploads.ConfirmDocumentsAsync(User.GetUserId(), CurrentRole(), request.UploadIds ?? [], cancellationToken);
        return result.ToActionResult();
    }

    private string CurrentRole() => User.IsInRole(Roles.Host) ? Roles.Host : Roles.Owner;
}

/// <summary>Corpo de <c>POST /api/v1/uploads</c>.</summary>
/// <param name="Purpose">Finalidade: <c>owner-avatar</c>, <c>host-avatar</c>, <c>pet-photo</c>, <c>listing-photo</c>, <c>chat-photo</c> ou <c>host-document</c>.</param>
/// <param name="ContentType"><c>image/jpeg</c>, <c>image/png</c> ou <c>image/webp</c>: o mesmo que vai no <c>PUT</c>.</param>
/// <param name="SizeBytes">Tamanho exato do arquivo (<c>file.size</c>), até 5 MB.</param>
/// <param name="ResourceId">O pet, o cantinho ou a conversa, conforme a finalidade.</param>
/// <param name="PhotoId">Foto a substituir (pet e cantinho). Sem ele, a foto é adicionada.</param>
/// <param name="Kind">Documento do anfitrião: <c>front</c>, <c>back</c> ou <c>selfie</c>.</param>
public sealed record RequestUploadRequest(
    string? Purpose,
    string? ContentType,
    long? SizeBytes,
    Guid? ResourceId,
    Guid? PhotoId,
    string? Kind);

/// <summary>Corpo (opcional) de <c>POST /api/v1/uploads/{uploadId}/confirm</c>.</summary>
/// <param name="Body">Legenda da foto do chat, até 2.000 caracteres. Ignorada nas outras finalidades.</param>
public sealed record ConfirmUploadRequest(string? Body);

/// <summary>One to three direct uploads of distinct host document slots, confirmed atomically.</summary>
public sealed record ConfirmDocumentsRequest(IReadOnlyList<Guid>? UploadIds);
