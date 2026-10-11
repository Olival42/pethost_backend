using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PetHost.Modules.Hosts.Application.Hosts.ChangeMyHostAvatar;
using PetHost.Modules.Hosts.Application.Hosts.DeactivateMyHost;
using PetHost.Modules.Hosts.Application.Hosts.ReactivateHost;
using PetHost.Modules.Hosts.Application.Hosts.Responses;
using PetHost.Modules.Hosts.Application.Hosts.UpdateMyHost;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Infrastructure.RateLimiting;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Modules.Hosts.Presentation.Hosts;

/// <summary>
/// O anfitrião cuidando da própria conta e perfil: editar (com a identidade travada), foto,
/// inativar e reativar. Mesmo molde do tutor.
/// </summary>
[ApiController]
[Route("api/v1/hosts")]
public sealed class HostProfileController(
    ICommandHandler<UpdateMyHostCommand, HostResponse> updateMyHost,
    ICommandHandler<ChangeMyHostAvatarCommand, HostResponse> changeMyAvatar,
    ICommandHandler<RemoveMyHostAvatarCommand, HostResponse> removeMyAvatar,
    ICommandHandler<DeactivateMyHostCommand, Unit> deactivateMyHost,
    ICommandHandler<ReactivateHostCommand, HostSessionResponse> reactivateHost) : ControllerBase
{
    /// <summary>Altera o anfitrião logado — só o que vier muda.</summary>
    /// <remarks>
    /// Livre: <c>phone</c>, <c>address</c> (pode vir pela metade) e, na empresa, <c>tradeName</c> e
    /// <c>companyAddress</c> (também parcial). <b>Travado</b> — conferido na verificação:
    /// <c>fullName</c>, <c>birthDate</c>, <c>cpf</c>, <c>cnpj</c> e <c>legalName</c>. Mandar o valor
    /// atual passa; mudar é 422 <c>HOST_IDENTITY_LOCKED</c> (nova verificação, pelo suporte).
    /// Pessoa física com dado de empresa: 422 <c>HOST_NOT_A_COMPANY</c>. A foto tem rota própria.
    /// </remarks>
    [HttpPatch("me")]
    [EnableRateLimiting(RateLimitPolicies.AccountUpdate)]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> UpdateMeAsync([FromBody] UpdateMyHostRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new UpdateMyHostCommand(
            User.GetUserId(),
            request.FullName,
            request.Phone,
            request.Address,
            request.BirthDate,
            request.Cpf,
            request.Cnpj,
            request.LegalName,
            request.TradeName,
            request.CompanyAddress);

        var result = await updateMyHost.HandleAsync(command, cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Troca a foto de perfil do anfitrião logado.</summary>
    /// <remarks>
    /// <c>multipart/form-data</c> com a imagem na parte <c>file</c>: JPG/JPEG, PNG ou WebP, até 5 MB.
    /// A URL pública fica em <c>user.avatarUrl</c>; a foto anterior é apagada.
    /// </remarks>
    [HttpPut("me/avatar")]
    [EnableRateLimiting(RateLimitPolicies.AccountUpdate)]
    [Authorize(Roles = Roles.Host)]
    [ImageUploadEndpoint]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ChangeMyAvatarAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        var result = await changeMyAvatar.HandleAsync(new ChangeMyHostAvatarCommand(User.GetUserId(), file.ToImageUpload()), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Tira a foto de perfil do anfitrião logado. Idempotente.</summary>
    [HttpDelete("me/avatar")]
    [EnableRateLimiting(RateLimitPolicies.AccountUpdate)]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<HostResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RemoveMyAvatarAsync(CancellationToken cancellationToken)
    {
        var result = await removeMyAvatar.HandleAsync(new RemoveMyHostAvatarCommand(User.GetUserId()), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Inativa a conta de anfitrião logada (e o perfil, se já aprovado).</summary>
    /// <remarks>
    /// As sessões caem na hora. A conta de tutor da mesma pessoa não é afetada. Para voltar:
    /// <c>POST /api/v1/hosts/reactivate</c>.
    /// </remarks>
    [HttpPost("me/deactivate")]
    [EnableRateLimiting(RateLimitPolicies.AccountSensitive)]
    [Authorize(Roles = Roles.Host)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> DeactivateMeAsync(CancellationToken cancellationToken)
    {
        var result = await deactivateMyHost.HandleAsync(new DeactivateMyHostCommand(User.GetUserId()), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Reativa a conta de anfitrião (e o perfil) com e-mail e senha, e já abre a sessão.</summary>
    /// <remarks>
    /// Credenciais erradas: o mesmo 401 do login. Conta suspensa pelo admin não volta: 403
    /// <c>AUTH_ACCOUNT_SUSPENDED</c>. <c>host</c> vem nulo se a conta ainda não foi aprovada.
    /// </remarks>
    [HttpPost("reactivate")]
    [EnableRateLimiting(RateLimitPolicies.Credentials)]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<HostSessionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<HostSessionResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<HostSessionResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<HostSessionResponse>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<HostSessionResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ReactivateAsync([FromBody] ReactivateHostCommand command, CancellationToken cancellationToken)
    {
        var result = await reactivateHost.HandleAsync(command, cancellationToken);

        return result.ToActionResult();
    }
}

/// <summary>Corpo do <c>PATCH /hosts/me</c>: só o que vier muda. O id vem do token, não daqui.</summary>
/// <param name="FullName">Travado: só passa se for igual ao atual.</param>
/// <param name="BirthDate">Travado: só passa se for igual ao atual.</param>
/// <param name="Cpf">Travado: só passa se for igual ao atual.</param>
/// <param name="Cnpj">Travado: só passa se for igual ao atual.</param>
/// <param name="LegalName">Travado: só passa se for igual ao atual.</param>
/// <param name="TradeName">Nome fantasia. Só empresa.</param>
/// <param name="CompanyAddress">Endereço da empresa, pode vir pela metade. Só empresa.</param>
public sealed record UpdateMyHostRequest(
    string? FullName = null,
    string? Phone = null,
    AddressData? Address = null,
    string? BirthDate = null,
    string? Cpf = null,
    string? Cnpj = null,
    string? LegalName = null,
    string? TradeName = null,
    AddressData? CompanyAddress = null);
