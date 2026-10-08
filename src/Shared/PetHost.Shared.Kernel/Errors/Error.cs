namespace PetHost.Shared.Kernel.Errors;

/// <param name="Code">Código estável, legível por máquina (SCREAMING_SNAKE_CASE).</param>
/// <param name="Message">Mensagem em inglês, frase completa.</param>
/// <param name="Field">Campo do payload em camelCase. Só em erro de validação.</param>
public sealed record Error(string Code, string Message, string? Field = null)
{
    public static Error Validation(string field, string message) => new(ErrorCodes.Validation, message, field);
    public static Error NotFound(string message) => new(ErrorCodes.NotFound, message);
    public static Error Conflict(string message) => new(ErrorCodes.Conflict, message);
    public static Error Unauthorized(string message) => new(ErrorCodes.Unauthorized, message);
    public static Error Forbidden(string message) => new(ErrorCodes.Forbidden, message);

    public static Error Unexpected(string message = "An unexpected error occurred.") =>
        new(ErrorCodes.Unexpected, message);
}
