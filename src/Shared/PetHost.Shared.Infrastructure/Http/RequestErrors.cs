using PetHost.Shared.Kernel.Errors;

namespace PetHost.Shared.Infrastructure.Http;

/// <summary>
/// Erros de validação que nascem antes do caso de uso, quando o corpo da requisição não pôde ser lido.
/// Renomear ou remover um código aqui é breaking change (§8).
/// </summary>
public static class RequestErrors
{
    public const string BodyField = "body";

    /// <summary>JSON malformado ou corpo vazio.</summary>
    public static readonly Error BodyInvalid =
        Error.Validation(BodyField, "REQUEST_BODY_INVALID", "The request body is missing or is not valid JSON.");

    /// <summary>O JSON é válido, mas o valor do campo tem tipo ou formato errado (número onde era texto).</summary>
    public static Error ValueInvalid(string field) =>
        Error.Validation(field, "REQUEST_VALUE_INVALID", "The value has an invalid type or format.");
}
