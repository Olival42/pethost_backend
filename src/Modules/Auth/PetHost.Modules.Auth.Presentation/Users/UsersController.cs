using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PetHost.Modules.Auth.Application.Sessions.Responses;
using PetHost.Modules.Auth.Application.Users.ChangePassword;
using PetHost.Modules.Auth.Application.Users.GetCurrentUser;
using PetHost.Modules.Auth.Application.Users.GetLinkedAccounts;
using PetHost.Modules.Auth.Application.Users.RegisterAccount;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Infrastructure.RateLimiting;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Auth.Presentation.Users;

/// <summary>
/// Contas de usuário. <c>me</c> é sempre a conta do access token: ninguém lê nem altera
/// a conta de outra pessoa por aqui. Conta de tutor é criada, inativada e reativada em
/// <c>/owners</c>, junto com o perfil de tutor; anfitrião ainda não inativa a conta.
/// </summary>
[ApiController]
[Route("api/v1/users")]
public sealed class UsersController(
    ICommandHandler<RegisterAccountCommand, SessionResponse> registerAccount,
    IQueryHandler<GetCurrentUserQuery, AuthenticatedUserResponse> getCurrentUser,
    IQueryHandler<GetLinkedAccountsQuery, IReadOnlyList<LinkedAccountResponse>> getLinkedAccounts,
    ICommandHandler<ChangePasswordCommand, SessionResponse> changePassword) : ControllerBase
{
    /// <summary>Onde a conta do token é lida: vai no <c>Location</c> do cadastro.</summary>
    public const string MeRoute = "/api/v1/users/me";

    /// <summary>Cria a conta de anfitrião e já abre a sessão.</summary>
    /// <remarks>
    /// Nome, e-mail, senha forte, telefone, nascimento (18+) e endereço completo. Todos os
    /// erros de formato voltam juntos num só 400. E-mail já usado como anfitrião devolve
    /// 409. O tutor se cadastra em <c>POST /api/v1/owners/register</c>.
    /// </remarks>
    [HttpPost("register")]
    [EnableRateLimiting(RateLimitPolicies.Registration)]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RegisterAsync(
        [FromBody] RegisterAccountRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = new RegisterAccountCommand(
            request.FullName,
            request.Email,
            request.Password,
            Roles.Host,
            request.Phone,
            request.BirthDate,
            request.Address);

        var result = await registerAccount.HandleAsync(command, cancellationToken);

        // A conta criada se lê em /users/me, com o access token que veio na resposta.
        return result.ToCreatedResult(MeRoute);
    }

    /// <summary>Dados da conta do token.</summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<ApiResponse<AuthenticatedUserResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<AuthenticatedUserResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<AuthenticatedUserResponse>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMeAsync(CancellationToken cancellationToken)
    {
        var result = await getCurrentUser.HandleAsync(new GetCurrentUserQuery(User.GetUserId()), cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Troca a senha da conta do token.</summary>
    /// <remarks>
    /// Pede a senha atual (errada: 400 no campo <c>currentPassword</c>) e uma senha nova
    /// forte e diferente. Depois da troca, <b>todas</b> as sessões da conta são encerradas
    /// e a resposta traz uma sessão nova; a pessoa recebe um e-mail avisando. Vale só para
    /// esta conta.
    /// </remarks>
    [HttpPost("me/password")]
    [EnableRateLimiting(RateLimitPolicies.AccountSensitive)]
    [Authorize]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ChangePasswordAsync(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await changePassword.HandleAsync(
            new ChangePasswordCommand(User.GetUserId(), request.CurrentPassword, request.NewPassword),
            cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Contas da mesma pessoa (tutor e/ou anfitrião), para o seletor de contas.</summary>
    [HttpGet("me/accounts")]
    [Authorize]
    [ProducesResponseType<ApiResponse<IReadOnlyList<LinkedAccountResponse>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<IReadOnlyList<LinkedAccountResponse>>>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyAccountsAsync(CancellationToken cancellationToken)
    {
        var result = await getLinkedAccounts.HandleAsync(new GetLinkedAccountsQuery(User.GetUserId()), cancellationToken);

        return result.ToActionResult();
    }
}

/// <summary>Corpo do cadastro de anfitrião. O papel é sempre <c>host</c>.</summary>
/// <param name="BirthDate">Texto <c>yyyy-MM-dd</c>, validado como os outros campos.</param>
public sealed record RegisterAccountRequest(
    string? FullName,
    string? Email,
    string? Password,
    string? Phone,
    string? BirthDate,
    AddressData? Address);

/// <summary>Corpo da troca de senha logada.</summary>
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
