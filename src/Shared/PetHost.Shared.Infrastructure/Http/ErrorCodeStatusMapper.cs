using Microsoft.AspNetCore.Http;
using PetHost.Shared.Kernel.Errors;

namespace PetHost.Shared.Infrastructure.Http;

/// <summary>Tradução código de erro → status HTTP (§7).</summary>
/// <remarks>
/// A seção 7 define a tabela dos códigos transversais e o mecanismo de sufixo
/// para códigos de módulo, com os exemplos <c>_NOT_FOUND</c> e <c>_CONFLICT</c>.
/// A lista de sufixos abaixo <b>estende</b> esse mecanismo, porque só com aqueles
/// dois os códigos que a própria seção 8 cita cairiam no status errado:
/// <c>AUTH_INVALID_CREDENTIALS</c> viraria 422 em vez de 401 e
/// <c>AUTH_EMAIL_ALREADY_REGISTERED</c> viraria 422 em vez de 409.
/// A regra segue genérica — vale para qualquer módulo, não só Auth.
/// </remarks>
public static class ErrorCodeStatusMapper
{
    /// <summary>
    /// Sufixos reconhecidos, do mais específico para o mais genérico. A ordem
    /// importa: <c>_TOKEN_INVALID</c> tem de ser testado antes de um eventual
    /// <c>_INVALID</c>.
    /// </summary>
    private static readonly (string Suffix, int StatusCode)[] SuffixStatusCodes =
    [
        ("_NOT_FOUND", StatusCodes.Status404NotFound),

        ("_CONFLICT", StatusCodes.Status409Conflict),
        ("_ALREADY_EXISTS", StatusCodes.Status409Conflict),
        ("_ALREADY_REGISTERED", StatusCodes.Status409Conflict),

        ("_UNAUTHORIZED", StatusCodes.Status401Unauthorized),
        ("_INVALID_CREDENTIALS", StatusCodes.Status401Unauthorized),
        ("_TOKEN_INVALID", StatusCodes.Status401Unauthorized),
        ("_TOKEN_EXPIRED", StatusCodes.Status401Unauthorized),

        ("_FORBIDDEN", StatusCodes.Status403Forbidden),
        ("_DEACTIVATED", StatusCodes.Status403Forbidden),
        ("_SUSPENDED", StatusCodes.Status403Forbidden),
        ("_NOT_OWNED_BY_REQUESTER", StatusCodes.Status403Forbidden),
    ];

    public static int ToHttpStatusCode(this string errorCode)
    {
        switch (errorCode)
        {
            case ErrorCodes.Validation:
                return StatusCodes.Status400BadRequest;
            case ErrorCodes.Unauthorized:
                return StatusCodes.Status401Unauthorized;
            case ErrorCodes.Forbidden:
                return StatusCodes.Status403Forbidden;
            case ErrorCodes.NotFound:
                return StatusCodes.Status404NotFound;
            case ErrorCodes.Conflict:
                return StatusCodes.Status409Conflict;
            case ErrorCodes.MethodNotAllowed:
                return StatusCodes.Status405MethodNotAllowed;
            case ErrorCodes.UnsupportedMediaType:
                return StatusCodes.Status415UnsupportedMediaType;
            case ErrorCodes.TooManyRequests:
                return StatusCodes.Status429TooManyRequests;
            case ErrorCodes.BusinessRule:
                return StatusCodes.Status422UnprocessableEntity;
            case ErrorCodes.Unexpected:
                return StatusCodes.Status500InternalServerError;
        }

        foreach (var (suffix, statusCode) in SuffixStatusCodes)
        {
            if (errorCode is not null && errorCode.EndsWith(suffix, StringComparison.Ordinal))
                return statusCode;
        }

        // Fallback: regra de negócio, não falha do servidor.
        return StatusCodes.Status422UnprocessableEntity;
    }
}
