using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PetHost.Modules.Owners.Application.Owners.DeactivateMyOwner;
using PetHost.Modules.Owners.Application.Owners.GetMyOwner;
using PetHost.Modules.Owners.Application.Owners.GetOwnerById;
using PetHost.Modules.Owners.Application.Owners.ListOwners;
using PetHost.Modules.Owners.Application.Owners.ReactivateOwner;
using PetHost.Modules.Owners.Application.Owners.RegisterOwnerAccount;
using PetHost.Modules.Owners.Application.Owners.Responses;
using PetHost.Modules.Owners.Application.Owners.SuspendOwner;
using PetHost.Modules.Owners.Application.Owners.UpdateMyOwner;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Infrastructure.RateLimiting;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Modules.Owners.Presentation.Owners;

/// <summary>Tutores: conta e perfil de tutor (CPF) tratados juntos — cadastro, edição, status.</summary>
[ApiController]
[Route("api/v1/owners")]
public sealed class OwnersController(
    ICommandHandler<RegisterOwnerAccountCommand, OwnerSessionResponse> registerOwnerAccount,
    ICommandHandler<UpdateMyOwnerCommand, OwnerResponse> updateMyOwner,
    ICommandHandler<DeactivateMyOwnerCommand, Unit> deactivateMyOwner,
    ICommandHandler<ReactivateOwnerCommand, OwnerSessionResponse> reactivateOwner,
    ICommandHandler<SuspendOwnerCommand, OwnerResponse> suspendOwner,
    ICommandHandler<LiftOwnerSuspensionCommand, OwnerResponse> liftOwnerSuspension,
    ICommandHandler<ReleaseOwnerCpfCommand, OwnerResponse> releaseOwnerCpf,
    IQueryHandler<GetMyOwnerQuery, OwnerResponse> getMyOwner,
    IQueryHandler<GetOwnerByIdQuery, OwnerResponse> getOwnerById,
    IQueryHandler<ListOwnersQuery, IReadOnlyList<OwnerResponse>> listOwners) : ControllerBase
{
    /// <summary>Onde o perfil do token é lido: vai no <c>Location</c> do cadastro.</summary>
    public const string MeRoute = "/api/v1/owners/me";

    /// <summary>Cadastra um tutor num pedido só — conta e CPF — e já abre a sessão.</summary>
    /// <remarks>
    /// Nome, e-mail, senha forte, telefone, nascimento (18+), endereço completo e CPF.
    /// Todos os erros de formato voltam juntos num só 400. E-mail já usado como tutor
    /// ou CPF já cadastrado devolvem 409.
    /// </remarks>
    [HttpPost("register")]
    [EnableRateLimiting(RateLimitPolicies.Registration)]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<OwnerSessionResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<OwnerSessionResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<OwnerSessionResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<OwnerSessionResponse>>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ApiResponse<OwnerSessionResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RegisterAccountAsync(
        [FromBody] RegisterOwnerAccountCommand command,
        CancellationToken cancellationToken)
    {
        var result = await registerOwnerAccount.HandleAsync(command, cancellationToken);

        return result.ToCreatedResult(MeRoute);
    }

    /// <summary>Perfil de tutor da conta logada.</summary>
    /// <remarks>404 só para conta de tutor antiga, de antes do cadastro unificado, sem CPF.</remarks>
    [HttpGet("me")]
    [Authorize(Roles = Roles.Owner)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMeAsync(CancellationToken cancellationToken)
    {
        var result = await getMyOwner.HandleAsync(new GetMyOwnerQuery(User.GetUserId()), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Altera o tutor logado: dados da conta, nascimento e/ou CPF, num pedido só.</summary>
    /// <remarks>
    /// Só muda o que vier no corpo (PATCH); o endereço também pode vir pela metade. Para
    /// limpar a foto ou o complemento, envie <c>""</c>. Valor igual ao atual não conta
    /// como alteração. Todos os erros de formato voltam juntos num só 400. Trocar o CPF
    /// ou a data de nascimento pede <c>currentPassword</c> (400 se faltar ou estiver
    /// errada) e só vale até o primeiro pagamento (depois, 422 <c>OWNER_CPF_LOCKED</c> /
    /// <c>OWNER_BIRTH_DATE_LOCKED</c>); CPF de outro tutor devolve 409. E-mail e senha
    /// não mudam por aqui: a senha muda em <c>POST /api/v1/users/me/password</c>.
    /// </remarks>
    [HttpPatch("me")]
    [EnableRateLimiting(RateLimitPolicies.AccountUpdate)]
    [Authorize(Roles = Roles.Owner)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> UpdateMeAsync(
        [FromBody] UpdateMyOwnerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new UpdateMyOwnerCommand(
            User.GetUserId(),
            request.FullName,
            request.Phone,
            request.AvatarUrl,
            request.Address,
            request.BirthDate,
            request.Cpf,
            request.CurrentPassword);

        var result = await updateMyOwner.HandleAsync(command, cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Inativa o tutor logado: o perfil de tutor e a conta, juntos.</summary>
    /// <remarks>
    /// Todas as sessões da conta caem na hora. A conta de anfitrião da mesma pessoa não é
    /// afetada. Para voltar, use <c>POST /api/v1/owners/reactivate</c>.
    /// </remarks>
    [HttpPost("me/deactivate")]
    [EnableRateLimiting(RateLimitPolicies.AccountSensitive)]
    [Authorize(Roles = Roles.Owner)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> DeactivateMeAsync(CancellationToken cancellationToken)
    {
        var result = await deactivateMyOwner.HandleAsync(new DeactivateMyOwnerCommand(User.GetUserId()), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Reativa o tutor (conta e perfil) e já abre a sessão.</summary>
    /// <remarks>
    /// Pede e-mail e senha da conta de tutor, porque conta inativa não consegue entrar
    /// para pedir isso logada. Credenciais erradas devolvem o mesmo 401 do login.
    /// </remarks>
    [HttpPost("reactivate")]
    [EnableRateLimiting(RateLimitPolicies.Credentials)]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<OwnerSessionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<OwnerSessionResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<OwnerSessionResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<OwnerSessionResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<OwnerSessionResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ReactivateAsync(
        [FromBody] ReactivateOwnerCommand command,
        CancellationToken cancellationToken)
    {
        var result = await reactivateOwner.HandleAsync(command, cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Um tutor, com os dados da conta. Só admin.</summary>
    [HttpGet("{ownerId:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var result = await getOwnerById.HandleAsync(new GetOwnerByIdQuery(ownerId), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Suspende um tutor: perfil e conta. Só admin, com motivo.</summary>
    /// <remarks>
    /// Todas as sessões do tutor caem na hora; login responde <c>403 AUTH_ACCOUNT_SUSPENDED</c>
    /// e ele <b>não</b> consegue reativar sozinho. Idempotente: suspender de novo mantém a
    /// primeira suspensão. O motivo vai para a trilha de auditoria.
    /// </remarks>
    [HttpPost("{ownerId:guid}/suspension")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SuspendAsync(
        Guid ownerId,
        [FromBody] AdminReasonRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await suspendOwner.HandleAsync(
            new SuspendOwnerCommand(User.GetUserId(), ownerId, request.Reason),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Tira a suspensão de um tutor: perfil e conta. Só admin.</summary>
    /// <remarks>
    /// O tutor volta ao status que tinha (ativo ou inativo) e precisa entrar de novo.
    /// Tutor cujo CPF foi liberado não volta: 422 <c>OWNER_CPF_RELEASED</c>.
    /// </remarks>
    [HttpDelete("{ownerId:guid}/suspension")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> LiftSuspensionAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var result = await liftOwnerSuspension.HandleAsync(
            new LiftOwnerSuspensionCommand(User.GetUserId(), ownerId),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Libera o CPF de um tutor suspenso (disputa de CPF). Só admin, com motivo.</summary>
    /// <remarks>
    /// Para quando alguém cadastrou o CPF de outra pessoa: depois de conferir o documento,
    /// o admin suspende a conta indevida e libera o CPF, e o dono pode se cadastrar. Só de
    /// tutor suspenso (422 <c>OWNER_SUSPENSION_REQUIRED</c>); a suspensão desse tutor não sai
    /// mais. A trilha guarda o CPF mascarado.
    /// </remarks>
    [HttpPost("{ownerId:guid}/cpf-release")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<OwnerResponse>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ReleaseCpfAsync(
        Guid ownerId,
        [FromBody] AdminReasonRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await releaseOwnerCpf.HandleAsync(
            new ReleaseOwnerCpfCommand(User.GetUserId(), ownerId, request.Reason),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Todos os tutores, com os dados da conta. Só admin; sem paginação nem filtro no MVP.</summary>
    [HttpGet]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<OwnerResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<OwnerResponse>>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<OwnerResponse>>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListAsync(CancellationToken cancellationToken)
    {
        var result = await listOwners.HandleAsync(new ListOwnersQuery(), cancellationToken);

        return result.ToActionResult();
    }
}

/// <summary>Corpo do <c>PATCH /owners/me</c>: só o que vier muda. O id vem do token, não daqui.</summary>
/// <param name="BirthDate">Texto <c>yyyy-MM-dd</c>. Só muda até o primeiro pagamento.</param>
/// <param name="CurrentPassword">Obrigatória só quando o CPF ou a data de nascimento mudam.</param>
public sealed record UpdateMyOwnerRequest(
    string? FullName,
    string? Phone,
    string? AvatarUrl,
    AddressData? Address,
    string? BirthDate,
    string? Cpf,
    string? CurrentPassword);

/// <summary>Corpo das ações do admin que exigem motivo. Vai para a trilha de auditoria.</summary>
public sealed record AdminReasonRequest(string? Reason);
