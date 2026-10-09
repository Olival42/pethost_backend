using System.Globalization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Modules.Auth.Infrastructure.Security;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Api.Authentication;

/// <summary>
/// Faz o middleware do JWT responder no envelope da API.
/// </summary>
/// <remarks>
/// Por padrão um 401 do <c>JwtBearer</c> sai com corpo vazio e um header
/// <c>WWW-Authenticate</c>, o que quebraria a regra de que <b>toda</b> resposta
/// usa <c>ApiResponse</c> (§7). Aqui o 401 e o 403 passam a ter
/// <c>UNAUTHORIZED</c> e <c>FORBIDDEN</c> no mesmo formato dos outros erros.
/// <para>
/// Também confere a revogação: token de conta inativada ou com senha trocada depois
/// da emissão é recusado com o mesmo 401, mesmo antes de expirar.
/// </para>
/// </remarks>
internal static class JwtEnvelopeEvents
{
    public static JwtBearerEvents Create() => new()
    {
        OnTokenValidated = async context =>
        {
            var principal = context.Principal;
            var subject = principal?.FindFirst("sub")?.Value;
            if (!Guid.TryParse(subject, out var userId) || IssuedAt(principal) is not { } issuedAt)
            {
                context.Fail("The access token has no valid 'sub' or 'iat' claim.");
                return;
            }

            var revocations = context.HttpContext.RequestServices.GetRequiredService<IAccessTokenRevocationStore>();
            var revoked = await revocations.IsRevokedAsync(
                new UserId(userId),
                issuedAt,
                context.HttpContext.RequestAborted);

            if (revoked)
                context.Fail("The access token was revoked.");
        },

        OnChallenge = async context =>
        {
            // Impede o handler de escrever a resposta padrão depois desta.
            context.HandleResponse();

            await WriteAsync(
                context.Response,
                StatusCodes.Status401Unauthorized,
                ErrorCodes.Unauthorized,
                "Authentication is required to access this resource.");
        },

        OnForbidden = context => WriteAsync(
            context.Response,
            StatusCodes.Status403Forbidden,
            ErrorCodes.Forbidden,
            "You do not have permission to access this resource."),
    };

    /// <summary>
    /// Instante da emissão: em milissegundos (<c>iat_ms</c>) quando o token traz, senão o
    /// <c>iat</c> padrão, em segundos.
    /// </summary>
    private static DateTimeOffset? IssuedAt(System.Security.Claims.ClaimsPrincipal? principal)
    {
        var milliseconds = principal?.FindFirst(JwtClaimNames.IssuedAtMilliseconds)?.Value;
        if (long.TryParse(milliseconds, NumberStyles.None, CultureInfo.InvariantCulture, out var ms))
            return DateTimeOffset.FromUnixTimeMilliseconds(ms);

        var seconds = principal?.FindFirst("iat")?.Value;
        return long.TryParse(seconds, NumberStyles.None, CultureInfo.InvariantCulture, out var s)
            ? DateTimeOffset.FromUnixTimeSeconds(s)
            : null;
    }

    private static Task WriteAsync(HttpResponse response, int statusCode, string code, string message)
    {
        response.StatusCode = statusCode;

        return response.WriteAsJsonAsync(
            ApiResponse<Unit>.Fail(new ErrorResponse(code, message)));
    }
}
