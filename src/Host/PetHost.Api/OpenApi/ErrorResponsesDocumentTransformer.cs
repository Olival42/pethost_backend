using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PetHost.Shared.Kernel.Errors;

namespace PetHost.Api.OpenApi;

/// <summary>
/// Dá a cada resposta do documento uma descrição em português e, nos erros (4xx e 5xx),
/// o envelope de erro (<c>success: false</c> + <c>error</c>) com um exemplo do status.
/// </summary>
/// <remarks>
/// O código do exemplo é o transversal do status (§7). O endpoint pode devolver um código
/// do módulo no lugar (<c>AUTH_INVALID_CREDENTIALS</c>, <c>BOOKING_NOT_FOUND</c>...), sempre
/// no mesmo formato — a lista de cada um fica no <c>modulo-*.md</c>.
/// </remarks>
internal sealed class ErrorResponsesDocumentTransformer : IOpenApiDocumentTransformer
{
    private const string ErrorEnvelopeSchema = "ApiErrorResponse";
    private const string ErrorSchema = "ApiError";
    private const string FieldErrorsSchema = "ApiFieldErrors";
    private const string JsonContentType = "application/json";
    private const string ExampleTimestamp = "2026-10-21T12:00:00+00:00";

    private static readonly Dictionary<int, string> Descriptions = new()
    {
        [StatusCodes.Status200OK] = "Sucesso.",
        [StatusCodes.Status201Created] = "Criado. O header Location aponta para o recurso.",
        [StatusCodes.Status400BadRequest] = "Dados inválidos. As mensagens vêm por campo em error.details.",
        [StatusCodes.Status401Unauthorized] = "Sem token, token inválido ou expirado, ou credenciais erradas.",
        [StatusCodes.Status403Forbidden] = "Sem permissão para esta ação, ou conta inativa/suspensa.",
        [StatusCodes.Status404NotFound] = "Recurso não encontrado (ou não visível para quem pediu).",
        [StatusCodes.Status409Conflict] = "Conflito com o estado atual (duplicado ou alterado por outro pedido).",
        [StatusCodes.Status413PayloadTooLarge] = "Corpo da requisição grande demais.",
        [StatusCodes.Status415UnsupportedMediaType] = "Content-Type não aceito.",
        [StatusCodes.Status422UnprocessableEntity] = "Regra de negócio impede a ação. O motivo vem em error.code.",
        [StatusCodes.Status429TooManyRequests] = "Muitas requisições. Espere o tempo do header Retry-After.",
        [StatusCodes.Status500InternalServerError] = "Erro inesperado. Informe o traceId ao suporte.",
        [StatusCodes.Status503ServiceUnavailable] = "Serviço externo fora do ar. Tente de novo em instantes.",
    };

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        AddErrorSchemas(document);

        foreach (var path in document.Paths.Values)
        {
            foreach (var operation in path.Operations?.Values ?? Enumerable.Empty<OpenApiOperation>())
            {
                foreach (var (statusText, response) in operation.Responses ?? [])
                {
                    if (response is not OpenApiResponse concrete
                        || !int.TryParse(statusText, NumberStyles.None, CultureInfo.InvariantCulture, out var status))
                        continue;

                    if (Descriptions.TryGetValue(status, out var description))
                        concrete.Description = description;

                    if (status < StatusCodes.Status400BadRequest)
                    {
                        // A API só responde JSON: o text/plain e o text/json do MVC são ruído.
                        if (concrete.Content?.TryGetValue(JsonContentType, out var json) == true)
                        {
                            concrete.Content = new Dictionary<string, OpenApiMediaType> { [JsonContentType] = json };
                            RequireData(json.Schema);
                        }

                        continue;
                    }

                    concrete.Content = new Dictionary<string, OpenApiMediaType>
                    {
                        [JsonContentType] = new()
                        {
                            Schema = new OpenApiSchemaReference(ErrorEnvelopeSchema, document),
                            Example = Example(status),
                        },
                    };
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// No sucesso o <c>data</c> sempre vem: tira o "ou null" do envelope, senão o exemplo
    /// do Swagger sai com <c>data: null</c>.
    /// </summary>
    private static void RequireData(IOpenApiSchema? envelope)
    {
        var target = envelope is OpenApiSchemaReference reference ? reference.Target : envelope;
        if (target is not OpenApiSchema { Properties: { } properties }
            || !properties.TryGetValue("data", out var data)
            || data is not OpenApiSchema { OneOf: { } options })
            return;

        var value = options.Where(option => option is not OpenApiSchema { Type: JsonSchemaType.Null }).ToList();
        if (value.Count == 1)
            properties["data"] = value[0];
    }

    private static void AddErrorSchemas(OpenApiDocument document)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();

        // Era o error do ApiResponse<T>, que agora descreve só o sucesso.
        document.Components.Schemas.Remove("ErrorResponse");

        document.Components.Schemas[FieldErrorsSchema] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description = "Mensagens de validação de um campo.",
            Required = new HashSet<string>(StringComparer.Ordinal) { "field", "messages" },
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["field"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Campo do corpo, em camelCase." },
                ["messages"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Items = new OpenApiSchema { Type = JsonSchemaType.String },
                },
            },
        };

        document.Components.Schemas[ErrorSchema] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Required = new HashSet<string>(StringComparer.Ordinal) { "code", "message" },
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["code"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Description = "Código estável (SCREAMING_SNAKE_CASE). É por ele que o front decide o que fazer.",
                },
                ["message"] = new OpenApiSchema { Type = JsonSchemaType.String, Description = "Mensagem em inglês, para log." },
                ["details"] = new OpenApiSchema
                {
                    Description = "Só em alguns erros: a lista por campo no VALIDATION_ERROR, o traceId no UNEXPECTED_ERROR.",
                    OneOf =
                    [
                        new OpenApiSchema { Type = JsonSchemaType.Array, Items = new OpenApiSchemaReference(FieldErrorsSchema, document) },
                        new OpenApiSchema { Type = JsonSchemaType.Object },
                    ],
                },
            },
        };

        document.Components.Schemas[ErrorEnvelopeSchema] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description = "Resposta de erro.",
            Required = new HashSet<string>(StringComparer.Ordinal) { "success", "error", "timestamp" },
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["success"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Boolean,
                    Description = "Sempre false no erro.",
                    Enum = [JsonValue.Create(false)],
                    Examples = [JsonValue.Create(false)],
                },
                ["error"] = new OpenApiSchemaReference(ErrorSchema, document),
                ["timestamp"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time" },
            },
        };
    }

    private static JsonObject Example(int status)
    {
        var error = status switch
        {
            StatusCodes.Status400BadRequest => Error(
                ErrorCodes.Validation,
                "Validation failed",
                new JsonArray(new JsonObject
                {
                    ["field"] = "email",
                    ["messages"] = new JsonArray("Email is required."),
                })),
            StatusCodes.Status401Unauthorized => Error(ErrorCodes.Unauthorized, "Authentication is required to access this resource."),
            StatusCodes.Status403Forbidden => Error(ErrorCodes.Forbidden, "You do not have permission to access this resource."),
            StatusCodes.Status404NotFound => Error(ErrorCodes.NotFound, "The requested resource was not found."),
            StatusCodes.Status409Conflict => Error(ErrorCodes.Conflict, "The request conflicts with the current state of the resource."),
            StatusCodes.Status413PayloadTooLarge => Error(ErrorCodes.PayloadTooLarge, "The request body must be at most 8 MB. Images up to 5 MB are accepted."),
            StatusCodes.Status415UnsupportedMediaType => Error(ErrorCodes.UnsupportedMediaType, "Unsupported content type. Send multipart/form-data, with the image in the 'file' field."),
            StatusCodes.Status422UnprocessableEntity => Error(ErrorCodes.BusinessRule, "The request violates a business rule."),
            StatusCodes.Status429TooManyRequests => Error(ErrorCodes.TooManyRequests, "Too many requests. Try again in 60 seconds."),
            StatusCodes.Status500InternalServerError => Error(
                ErrorCodes.Unexpected,
                "An unexpected error occurred.",
                new JsonObject { ["traceId"] = "0HN7A1B2C3D4E:00000001" }),
            StatusCodes.Status503ServiceUnavailable => Error("LISTING_GEOCODER_UNAVAILABLE", "The map service is unavailable right now. Try again in a moment."),
            _ => Error(ErrorCodes.Unexpected, "An unexpected error occurred."),
        };

        return new JsonObject
        {
            ["success"] = false,
            ["error"] = error,
            ["timestamp"] = ExampleTimestamp,
        };
    }

    private static JsonObject Error(string code, string message, JsonNode? details = null)
    {
        var error = new JsonObject { ["code"] = code, ["message"] = message };
        if (details is not null)
            error["details"] = details;

        return error;
    }
}
