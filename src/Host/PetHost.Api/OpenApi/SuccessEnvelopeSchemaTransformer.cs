using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PetHost.Shared.Contracts.Responses;

namespace PetHost.Api.OpenApi;

/// <summary>
/// Faz o schema de <c>ApiResponse&lt;T&gt;</c> descrever só a resposta de sucesso:
/// <c>success: true</c>, <c>data</c> e <c>timestamp</c>, sem o <c>error</c>.
/// </summary>
/// <remarks>
/// Os controllers declaram o mesmo <c>ApiResponse&lt;T&gt;</c> em todos os status, então o
/// Swagger mostrava o mesmo exemplo no 200 e nos erros. As respostas de erro ganham o
/// schema próprio em <see cref="ErrorResponsesDocumentTransformer"/>.
/// </remarks>
internal sealed class SuccessEnvelopeSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        var type = context.JsonTypeInfo.Type;
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(ApiResponse<>) || schema.Properties is null)
            return Task.CompletedTask;

        schema.Description = "Resposta de sucesso.";
        schema.Properties.Remove("error");

        if (schema.Properties.TryGetValue("success", out var success) && success is OpenApiSchema successSchema)
        {
            successSchema.Default = null;
            successSchema.Enum = [JsonValue.Create(true)];
            successSchema.Examples = [JsonValue.Create(true)];
            successSchema.Description = "Sempre true no sucesso.";
        }

        schema.Required = new HashSet<string>(StringComparer.Ordinal) { "success", "data", "timestamp" };

        return Task.CompletedTask;
    }
}
