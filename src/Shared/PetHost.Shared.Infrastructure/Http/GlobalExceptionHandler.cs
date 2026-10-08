using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Shared.Infrastructure.Http;

/// <summary>
/// Único lugar que captura exceção não tratada (§15). Loga com o TraceId e devolve
/// <c>UNEXPECTED_ERROR</c> + 500. <c>Details</c> leva apenas o traceId — nunca stack
/// trace, mensagem de exceção ou nome de tabela.
/// </summary>
public sealed partial class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Error,
        Message = "Unhandled exception on {Method} {Path}. TraceId {TraceId}.")]
    private static partial void LogUnhandledException(
        ILogger logger, Exception exception, string method, string path, string traceId);

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = httpContext.TraceIdentifier;

        LogUnhandledException(logger, exception, httpContext.Request.Method, httpContext.Request.Path, traceId);

        var response = ApiResponse<Unit>.Fail(new ErrorResponse(
            ErrorCodes.Unexpected,
            "An unexpected error occurred.",
            new { traceId }));

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(response, cancellationToken).ConfigureAwait(false);

        return true;
    }
}
