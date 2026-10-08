using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PetHost.Modules.Auth.Application.Sessions.CreateSession;
using PetHost.Modules.Auth.Application.Sessions.RefreshSession;
using PetHost.Modules.Auth.Application.Sessions.Responses;
using PetHost.Modules.Auth.Application.Sessions.RevokeSession;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Modules.Auth.Presentation.Sessions;

/// <summary>Entrar, renovar e sair. Sessão é o recurso; não há verbo na rota (§13).</summary>
[ApiController]
[Route("api/v1/auth/sessions")]
public sealed class SessionsController(
    ICommandHandler<CreateSessionCommand, SessionResponse> createSession,
    ICommandHandler<RefreshSessionCommand, SessionResponse> refreshSession,
    ICommandHandler<RevokeSessionCommand, Unit> revokeSession) : ControllerBase
{
    /// <summary>Entra na conta e abre uma sessão.</summary>
    /// <remarks>
    /// O <c>role</c> é obrigatório: o e-mail não identifica a conta sozinho, porque
    /// o mesmo e-mail pode ter uma conta de tutor e uma de anfitrião. Corresponde à
    /// escolha "Sou tutor / Sou anfitrião" da tela de entrar.
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status500InternalServerError)]
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
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<SessionResponse>>(StatusCodes.Status500InternalServerError)]
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
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RevokeAsync(
        [FromBody] RevokeSessionCommand command,
        CancellationToken cancellationToken)
    {
        var result = await revokeSession.HandleAsync(command, cancellationToken);

        return result.ToActionResult();
    }
}
