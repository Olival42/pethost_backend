using Microsoft.AspNetCore.Authentication.JwtBearer;
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
/// </remarks>
internal static class JwtEnvelopeEvents
{
    public static JwtBearerEvents Create() => new()
    {
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

    private static Task WriteAsync(HttpResponse response, int statusCode, string code, string message)
    {
        response.StatusCode = statusCode;

        return response.WriteAsJsonAsync(
            ApiResponse<Unit>.Fail(new ErrorResponse(code, message)));
    }
}
