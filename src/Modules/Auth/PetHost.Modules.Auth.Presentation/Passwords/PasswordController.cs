using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using PetHost.Modules.Auth.Application.Passwords.ForgotPassword;
using PetHost.Modules.Auth.Application.Passwords.ResetPassword;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Infrastructure.Http;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Modules.Auth.Presentation.Passwords;

/// <summary>Esqueci a senha: pedir o token por e-mail e trocar a senha com ele.</summary>
[ApiController]
[Route("api/v1/auth/password")]
public sealed class PasswordController(
    ICommandHandler<ForgotPasswordCommand, Unit> forgotPassword,
    ICommandHandler<ResetPasswordCommand, Unit> resetPassword) : ControllerBase
{
    /// <summary>Manda por e-mail um token para trocar a senha.</summary>
    /// <remarks>
    /// Responde 200 exista a conta ou não — o endpoint não serve para descobrir quem
    /// tem cadastro. O token vale por <c>PasswordReset:TokenLifetimeMinutes</c>, é de
    /// uso único, e um pedido novo invalida o anterior.
    /// </remarks>
    [HttpPost("forgot")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ForgotAsync(
        [FromBody] ForgotPasswordCommand command,
        CancellationToken cancellationToken)
    {
        var result = await forgotPassword.HandleAsync(command, cancellationToken);

        return result.ToActionResult();
    }

    /// <summary>Troca a senha usando o token recebido por e-mail.</summary>
    /// <remarks>
    /// A senha nova precisa ser forte (8 a 128 caracteres, maiúscula, minúscula,
    /// número e caractere especial). Senha fraca devolve 400 sem gastar o token.
    /// Depois da troca, todas as sessões da conta são encerradas.
    /// </remarks>
    [HttpPost("reset")]
    [AllowAnonymous]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ApiResponse<Unit>>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ResetAsync(
        [FromBody] ResetPasswordCommand command,
        CancellationToken cancellationToken)
    {
        var result = await resetPassword.HandleAsync(command, cancellationToken);

        return result.ToActionResult();
    }
}
