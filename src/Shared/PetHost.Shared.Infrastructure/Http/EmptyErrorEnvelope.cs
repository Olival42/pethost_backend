using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Shared.Infrastructure.Http;

/// <summary>
/// Põe no envelope (§7) os erros que o ASP.NET Core responde <b>sem corpo</b>: rota que não
/// existe ou id fora do formato da rota (<c>/pets/abc</c> com <c>{petId:guid}</c>) → 404,
/// método errado → 405, content-type errado → 415.
/// </summary>
/// <remarks>
/// Só age quando a resposta é de erro e ainda não tem corpo. Os erros que já saem no envelope
/// (handlers, JWT, rate limit, exceção) passam intactos.
/// </remarks>
public static class EmptyErrorEnvelope
{
    public static IApplicationBuilder UseEmptyErrorEnvelope(this IApplicationBuilder app) =>
        app.UseStatusCodePages(context => WriteAsync(context.HttpContext));

    private static Task WriteAsync(HttpContext context)
    {
        var status = context.Response.StatusCode;
        var (code, message) = Describe(status);

        return context.Response.WriteAsJsonAsync(
            ApiResponse<Unit>.Fail(new ErrorResponse(code, message)),
            context.RequestAborted);
    }

    private static (string Code, string Message) Describe(int status) => status switch
    {
        StatusCodes.Status400BadRequest => (ErrorCodes.Validation, "The request is invalid."),
        StatusCodes.Status401Unauthorized => (ErrorCodes.Unauthorized, "Authentication is required to access this resource."),
        StatusCodes.Status403Forbidden => (ErrorCodes.Forbidden, "You do not have permission to access this resource."),
        StatusCodes.Status404NotFound => (ErrorCodes.NotFound, "The requested resource was not found."),
        StatusCodes.Status405MethodNotAllowed => (ErrorCodes.MethodNotAllowed, "This HTTP method is not allowed for this resource."),
        StatusCodes.Status413PayloadTooLarge => (ErrorCodes.PayloadTooLarge, "The request body is too large."),
        StatusCodes.Status415UnsupportedMediaType => (ErrorCodes.UnsupportedMediaType, "Unsupported content type. Send application/json."),
        StatusCodes.Status429TooManyRequests => (ErrorCodes.TooManyRequests, "Too many requests. Try again later."),
        >= StatusCodes.Status500InternalServerError => (ErrorCodes.Unexpected, "An unexpected error occurred."),

        // Raros (406, 413, 414...): o código sai da frase padrão do status, ex. PAYLOAD_TOO_LARGE.
        _ => FromReasonPhrase(status),
    };

    private static (string Code, string Message) FromReasonPhrase(int status)
    {
        var phrase = ReasonPhrases.GetReasonPhrase(status);
        if (string.IsNullOrEmpty(phrase))
            return (ErrorCodes.BusinessRule, "The request could not be processed.");

        var code = phrase.Replace(' ', '_').Replace("-", "_", StringComparison.Ordinal).ToUpperInvariant();

        return (code, $"{phrase}.");
    }
}
