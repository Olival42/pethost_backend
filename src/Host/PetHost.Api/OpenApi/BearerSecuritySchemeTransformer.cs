using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace PetHost.Api.OpenApi;

/// <summary>
/// Declara o JWT no documento OpenAPI: o Swagger UI ganha o botão <b>Authorize</b>,
/// onde se cola o <c>accessToken</c> (sem o prefixo "Bearer").
/// </summary>
/// <remarks>
/// O requisito vale para o documento todo: rota anônima simplesmente ignora o header.
/// Quem decide o acesso continua sendo o <c>[Authorize]</c>/<c>[AllowAnonymous]</c>.
/// </remarks>
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    private const string SchemeName = "Bearer";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Info.Title = "PetHost API";
        document.Info.Description = "Respostas no envelope { success, data, error, timestamp }. Para as rotas com token, faça login e use Authorize.";

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Cole o accessToken do login (POST /api/v1/auth/sessions/login).",
        };

        document.Security ??= [];
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeName, document)] = [],
        });

        return Task.CompletedTask;
    }
}
