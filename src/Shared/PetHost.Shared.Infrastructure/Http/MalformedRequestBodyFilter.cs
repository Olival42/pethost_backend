using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Shared.Infrastructure.Http;

/// <summary>
/// Responde 400 no envelope quando o corpo da requisição não pôde ser lido:
/// JSON malformado, corpo vazio ou valor com tipo errado.
/// </summary>
/// <remarks>
/// O host desliga o filtro automático do MVC (<c>SuppressModelStateInvalidFilter</c>,
/// §7) para que a validação seja nossa. O efeito colateral é que um JSON inválido
/// chegava ao handler como <c>null</c> e virava 500. Este filtro cobre só esse
/// caso; as regras de negócio continuam no FluentValidation.
/// <para>
/// Valor com tipo errado (<c>"phone": 123</c>) aponta o campo; JSON quebrado ou corpo
/// vazio aponta <c>body</c>. A mensagem do parser não é repassada: ela cita nomes de
/// tipos internos. O leitor de JSON para no primeiro valor inválido, então só esse
/// campo aparece — por isso os campos que costumam vir errados (como datas) são lidos
/// como texto e validados pelo FluentValidation, junto com os outros.
/// </para>
/// </remarks>
public sealed class MalformedRequestBodyFilter : IActionFilter
{
    public const string Field = "body";
    public const string Message = "The request body is missing or is not valid JSON.";
    public const string InvalidValueMessage = "The value has an invalid type or format.";

    /// <summary>Prefixo que o leitor de JSON põe no caminho do campo com problema.</summary>
    private const string JsonPathRoot = "$.";

    public void OnActionExecuting(ActionExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ModelState.IsValid)
            return;

        // Campo só quando o JSON é válido e o problema é o tipo do valor. JSON cortado
        // ou com sintaxe errada também vem com caminho, mas aí o culpado é o corpo.
        var fieldErrors = context.ModelState
            .Where(entry => entry.Key.StartsWith(JsonPathRoot, StringComparison.Ordinal)
                && entry.Value is { Errors.Count: > 0 } state
                && state.Errors.All(IsTypeConversionError))
            .Select(entry => new DataErrors(entry.Key[JsonPathRoot.Length..], [InvalidValueMessage]))
            .ToList();

        var details = fieldErrors.Count > 0
            ? fieldErrors
            : [new DataErrors(Field, [Message])];

        var response = ApiResponse<Unit>.Fail(new ErrorResponse(ErrorCodes.Validation, "Validation failed", details));

        context.Result = new ObjectResult(response) { StatusCode = StatusCodes.Status400BadRequest };
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }

    /// <summary>
    /// Valor que o <c>System.Text.Json</c> leu mas não conseguiu converter (número onde
    /// era texto, por exemplo). Erro de sintaxe — JSON cortado, vírgula sobrando — tem
    /// outro texto e cai em <c>body</c>.
    /// </summary>
    /// <remarks>
    /// O MVC guarda o erro do leitor só como texto (<see cref="ModelError.ErrorMessage"/>),
    /// sem a exceção; por isso a checagem é pela frase fixa do <c>System.Text.Json</c>.
    /// </remarks>
    private static bool IsTypeConversionError(ModelError error)
    {
        var message = error.Exception?.InnerException?.Message
            ?? error.Exception?.Message
            ?? error.ErrorMessage;

        return message.Contains("could not be converted", StringComparison.Ordinal);
    }
}
