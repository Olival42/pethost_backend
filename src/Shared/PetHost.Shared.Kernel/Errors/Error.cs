using System.Text.RegularExpressions;

namespace PetHost.Shared.Kernel.Errors;

/// <param name="Code">Código estável, legível por máquina (SCREAMING_SNAKE_CASE).</param>
/// <param name="Message">Mensagem em inglês, frase completa.</param>
/// <param name="Field">Campo do payload em camelCase. Só em erro de validação.</param>
/// <param name="Reason">
/// Motivo estável da regra de validação que falhou (<c>MODULO_CAMPO_REGRA</c>, ex.:
/// <c>BOOKING_CHECK_IN_TOO_SOON</c>). Só em erro de validação, onde o <paramref name="Code"/> de topo
/// é sempre <c>VALIDATION_ERROR</c>. É por ele que o front escolhe o texto.
/// </param>
/// <param name="Params">Números e textos que a frase da regra usa (limites, formatos). Só em erro de validação.</param>
public sealed record Error(
    string Code,
    string Message,
    string? Field = null,
    string? Reason = null,
    ErrorParams? Params = null)
{
    private static readonly Regex ReasonPattern = new(
        "^[A-Z][A-Z0-9]*(_[A-Z0-9]+)+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    /// <summary>
    /// Erro de validação de um campo. <paramref name="reason"/> é o código estável da regra e
    /// <paramref name="parameters"/> leva só o que a frase precisa (nunca o valor enviado).
    /// </summary>
    public static Error Validation(string field, string reason, string message, params (string Name, object Value)[] parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (!ReasonPattern.IsMatch(reason))
            throw new ArgumentException($"Validation reason '{reason}' must be SCREAMING_SNAKE_CASE (MODULE_FIELD_RULE).", nameof(reason));

        return new Error(ErrorCodes.Validation, message, field, reason, parameters.Length == 0 ? null : ErrorParams.Of(parameters));
    }

    public static Error NotFound(string message) => new(ErrorCodes.NotFound, message);
    public static Error Conflict(string message) => new(ErrorCodes.Conflict, message);
    public static Error Unauthorized(string message) => new(ErrorCodes.Unauthorized, message);
    public static Error Forbidden(string message) => new(ErrorCodes.Forbidden, message);

    public static Error Unexpected(string message = "An unexpected error occurred.") =>
        new(ErrorCodes.Unexpected, message);
}
