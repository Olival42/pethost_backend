using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
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
/// A mensagem do parser não é repassada: ela cita nomes de tipos internos.
/// </remarks>
public sealed class MalformedRequestBodyFilter : IActionFilter
{
    public const string Field = "body";
    public const string Message = "The request body is missing or is not valid JSON.";

    public void OnActionExecuting(ActionExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ModelState.IsValid)
            return;

        var response = ApiResponse<Unit>.Fail(new ErrorResponse(
            ErrorCodes.Validation,
            "Validation failed",
            new List<DataErrors> { new(Field, [Message]) }));

        context.Result = new ObjectResult(response) { StatusCode = StatusCodes.Status400BadRequest };
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
