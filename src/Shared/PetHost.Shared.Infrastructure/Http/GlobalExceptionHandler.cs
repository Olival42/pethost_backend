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
/// <para>
/// A exceção é do cliente quando é <see cref="BadHttpRequestException"/>: o Kestrel a lança, por
/// exemplo, com corpo acima do limite (30 MB). Nesse caso sai o status dela no envelope (413
/// <c>PAYLOAD_TOO_LARGE</c>, os outros <c>VALIDATION_ERROR</c>) e o log é Warning, não Error.
/// </para>
/// </summary>
public sealed partial class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    [LoggerMessage(
        EventId = 5000,
        Level = LogLevel.Error,
        Message = "Unhandled exception on {Method} {Path}. TraceId {TraceId}.")]
    private static partial void LogUnhandledException(
        ILogger logger, Exception exception, string method, string path, string traceId);

    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Warning,
        Message = "Bad request on {Method} {Path}: {StatusCode}. TraceId {TraceId}.")]
    private static partial void LogBadRequest(
        ILogger logger, string method, string path, int statusCode, string traceId);

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = httpContext.TraceIdentifier;

        if (exception is BadHttpRequestException badRequest)
        {
            LogBadRequest(logger, httpContext.Request.Method, httpContext.Request.Path, badRequest.StatusCode, traceId);

            var error = badRequest.StatusCode == StatusCodes.Status413PayloadTooLarge
                ? new ErrorResponse(ErrorCodes.PayloadTooLarge, "The request body is too large.")
                : new ErrorResponse(ErrorCodes.Validation, "The request is malformed.");

            httpContext.Response.StatusCode = badRequest.StatusCode;
            await httpContext.Response.WriteAsJsonAsync(ApiResponse<Unit>.Fail(error), cancellationToken).ConfigureAwait(false);

            return true;
        }

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
