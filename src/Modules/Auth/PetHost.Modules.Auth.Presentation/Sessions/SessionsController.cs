using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PetHost.Modules.Auth.Application.Sessions.CreateSession;
using PetHost.Modules.Auth.Application.Sessions.RefreshSession;
using PetHost.Modules.Auth.Application.Sessions.Responses;
using PetHost.Modules.Auth.Application.Sessions.RevokeSession;
using PetHost.Modules.Auth.Application.Sessions.SwitchSession;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Infrastructure.RateLimiting;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Modules.Auth.Presentation.Sessions;

/// <summary>Entrar, renovar e sair. Sessão é o recurso; não há verbo na rota (§13).</summary>
[ApiController]
[Route("api/v1/auth/sessions")]
public sealed class SessionsController(
    ICommandHandler<CreateSessionCommand, SessionResponse> createSession,
    ICommandHandler<RefreshSessionCommand, SessionResponse> refreshSession,
    ICommandHandler<RevokeSessionCommand, Unit> revokeSession,
    ICommandHandler<SwitchSessionCommand, SessionResponse> switchSession) : ControllerBase
{
    /// <summary>Entra na conta e abre uma sessão.</summary>
    /// <remarks>
    /// O <c>role</c> é obrigatório: o e-mail não identifica a conta sozinho, porque
    /// o mesmo e-mail pode ter uma conta de tutor e uma de anfitrião. Corresponde à
    /// escolha "Sou tutor / Sou anfitrião" da tela de entrar.
    /// </remarks>
    [HttpPost("login")]
    [EnableRateLimiting(RateLimitPolicies.Credentials)]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] CreateSessionCommand command,
        CancellationToken cancellationToken)
    {
        var result = await createSession.HandleAsync(command, cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Troca um refresh token válido por um par novo de tokens.</summary>
    /// <remarks>
    /// O refresh token é de uso único: o token enviado é invalidado nesta chamada,
    /// mesmo que ainda estivesse dentro do prazo.
    /// </remarks>
    [HttpPost("refresh")]
    [EnableRateLimiting(RateLimitPolicies.Session)]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RefreshAsync(
        [FromBody] RefreshSessionCommand command,
        CancellationToken cancellationToken)
    {
        var result = await refreshSession.HandleAsync(command, cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Sai da conta, invalidando o refresh token.</summary>
    /// <remarks>
    /// Idempotente: revogar um token que já não vale devolve 200. O access token
    /// que já foi emitido continua válido até expirar — é a natureza de um token
    /// sem estado, e o motivo de a vida dele ser curta.
    /// </remarks>
    [HttpPost("logout")]
    [EnableRateLimiting(RateLimitPolicies.Session)]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status500InternalServerError)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> RevokeAsync(
        [FromBody] RevokeSessionCommand command,
        CancellationToken cancellationToken)
    {
        var result = await revokeSession.HandleAsync(command, cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Troca para a outra conta da mesma pessoa (tutor ↔ anfitrião).</summary>
    /// <remarks>
    /// Pede a senha da conta de destino: enquanto o e-mail não é verificado, ter o
    /// mesmo e-mail não prova que é a mesma pessoa. A sessão atual continua valendo.
    /// </remarks>
    [HttpPost("switch")]
    [EnableRateLimiting(RateLimitPolicies.Credentials)]
    [Authorize]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SwitchAsync(
        [FromBody] SwitchSessionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await switchSession.HandleAsync(
            new SwitchSessionCommand(User.GetUserId(), request.Role, request.Password),
            cancellationToken);

        return result.ToActionResult();
    }
}

/// <summary>Corpo do <c>POST /sessions/switch</c>.</summary>
/// <param name="Role">Conta de destino: <c>owner</c> ou <c>host</c>.</param>
public sealed record SwitchSessionRequest(string? Role, string? Password);
