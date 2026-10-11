using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PetHost.Modules.Hosts.Application.Hosts.GetHostById;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Modules.Hosts.Application.Hosts.SuspendHost;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Hosts.Presentation.Hosts;

/// <summary>
/// O admin sobre os anfitriões verificados: listar, ver, suspender, tirar a suspensão e liberar
/// CPF/CNPJ. Tudo só admin; leitura e ação vão para a trilha de auditoria, ação com motivo.
/// </summary>
[ApiController]
[Route("api/v1/hosts")]
[Authorize(Roles = Roles.Admin)]
public sealed class HostsAdminController(
    IQueryHandler<ListHostsQuery, IReadOnlyList<HostResponse>> listHosts,
    IQueryHandler<GetHostByIdQuery, HostResponse> getHostById,
    ICommandHandler<SuspendHostCommand, HostResponse> suspendHost,
    ICommandHandler<LiftHostSuspensionCommand, HostResponse> liftHostSuspension,
    ICommandHandler<ReleaseHostIdentityCommand, HostResponse> releaseHostIdentity) : ControllerBase
{
    /// <summary>Todos os anfitriões, com os dados da conta. Sem paginação nem filtro no MVP.</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<HostResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<HostResponse>>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<HostResponse>>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken)
    {
        var result = await listHosts.HandleAsync(new ListHostsQuery(), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Um anfitrião, pelo <b>id do anfitrião</b>, com os dados da conta.</summary>
    [HttpGet("{hostId:guid}")]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdAsync(Guid hostId, CancellationToken cancellationToken)
    {
        var result = await getHostById.HandleAsync(new GetHostByIdQuery(hostId), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Suspende um anfitrião: perfil e conta. Com motivo.</summary>
    /// <remarks>
    /// As sessões caem na hora; login responde 403 <c>AUTH_ACCOUNT_SUSPENDED</c> e a pessoa não
    /// reativa sozinha. Idempotente.
    /// </remarks>
    [HttpPost("{hostId:guid}/suspension")]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SuspendAsync(Guid hostId, [FromBody] HostAdminReasonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await suspendHost.HandleAsync(new SuspendHostCommand(User.GetUserId(), hostId, request.Reason), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Tira a suspensão: perfil e conta.</summary>
    /// <remarks>Anfitrião com CPF/CNPJ liberado não volta: 422 <c>HOST_IDENTITY_RELEASED</c>.</remarks>
    [HttpDelete("{hostId:guid}/suspension")]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> LiftSuspensionAsync(Guid hostId, CancellationToken cancellationToken)
    {
        var result = await liftHostSuspension.HandleAsync(new LiftHostSuspensionCommand(User.GetUserId(), hostId), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Libera o CPF/CNPJ de um anfitrião suspenso (disputa de documento). Com motivo.</summary>
    /// <remarks>
    /// Pessoa física: o CPF. Empresa: o CNPJ e o CPF do representante. Os valores ficam no perfil
    /// como evidência, mas deixam de contar — o dono de verdade pode se cadastrar. Só de anfitrião
    /// suspenso (422 <c>HOST_SUSPENSION_REQUIRED</c>); a suspensão desse perfil não sai mais.
    /// </remarks>
    [HttpPost("{hostId:guid}/identity-release")]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ReleaseIdentityAsync(Guid hostId, [FromBody] HostAdminReasonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await releaseHostIdentity.HandleAsync(new ReleaseHostIdentityCommand(User.GetUserId(), hostId, request.Reason), cancellationToken);

        return result.ToActionResult();
    }
}

/// <summary>Corpo das ações do admin que exigem motivo. Vai para a trilha de auditoria.</summary>
public sealed record HostAdminReasonRequest(string? Reason);
