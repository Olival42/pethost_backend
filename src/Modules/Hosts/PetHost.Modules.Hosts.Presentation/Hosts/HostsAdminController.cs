using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PetHost.Modules.Hosts.Application.Hosts.GetHostById;
using PetHost.Modules.Hosts.Application.Hosts.ResetHostStripeAccount;
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
    ICommandHandler<ReleaseHostIdentityCommand, HostResponse> releaseHostIdentity,
    ICommandHandler<ResetHostStripeAccountCommand, HostResponse> resetHostStripeAccount) : ControllerBase
{
    /// <summary>Todos os anfitriões, com os dados da conta. Sem paginação no MVP.</summary>
    /// <remarks>
    /// <c>?stripeStatus=</c> filtra pela situação do cadastro de recebimento: <c>not_started</c>,
    /// <c>in_progress</c>, <c>under_review</c>, <c>restricted</c>, <c>rejected</c> ou <c>enabled</c>
    /// (ex.: <c>in_progress</c> = quem começou e travou). Valor desconhecido: 400 em <c>stripeStatus</c>.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<ApiResponse<IReadOnlyList<HostResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<HostResponse>>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<HostResponse>>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<HostResponse>>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListAsync([FromQuery] string? stripeStatus, CancellationToken cancellationToken)
    {
        var result = await listHosts.HandleAsync(new ListHostsQuery(stripeStatus), cancellationToken);

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

    /// <summary>Desliga a conta do Stripe recusada, para o anfitrião cadastrar o recebimento de novo. Com motivo.</summary>
    /// <remarks>
    /// Para recusa por engano ou por dado digitado errado (<c>rejected.other</c>, por exemplo). Fraude
    /// (<c>rejected.fraud</c>) pede suspensão, não nova chance. A conta recusada fica no histórico
    /// (<c>host.stripe_account_reset</c> na trilha); o anfitrião volta para <c>not_started</c> e a
    /// próxima abertura do cadastro cria uma conta nova no Stripe. Só de conta recusada:
    /// 422 <c>HOST_STRIPE_ACCOUNT_NOT_REJECTED</c>.
    /// </remarks>
    [HttpPost("{hostId:guid}/stripe-account-reset")]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ResetStripeAccountAsync(Guid hostId, [FromBody] HostAdminReasonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await resetHostStripeAccount.HandleAsync(new ResetHostStripeAccountCommand(User.GetUserId(), hostId, request.Reason), cancellationToken);

        return result.ToActionResult();
    }
}

/// <summary>Corpo das ações do admin que exigem motivo. Vai para a trilha de auditoria.</summary>
public sealed record HostAdminReasonRequest(string? Reason);
