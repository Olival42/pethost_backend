using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PetHost.Shared.Infrastructure.Cors;

/// <summary>
/// CORS para o front (Next.js no navegador): uma política padrão, aplicada a toda a API e ao
/// hub (<c>app.UseCors()</c> no host), com as origens de <see cref="FrontendCorsOptions"/>.
/// </summary>
/// <remarks>
/// Qualquer método e header (o <c>Authorization</c> do JWT, o <c>Content-Type</c> do JSON e do
/// upload). Com credenciais: o cliente do SignalR negocia com <c>withCredentials</c>, e o
/// navegador recusa credencial com origem <c>*</c> — por isso a lista é explícita. O front lê
/// <c>Location</c> (recurso criado) e <c>Retry-After</c> (429), expostos aqui.
/// </remarks>
public static class CorsServiceCollectionExtensions
{
    /// <summary>Headers da resposta que o JavaScript do front pode ler, além dos básicos.</summary>
    public static IReadOnlyList<string> ExposedHeaders { get; } = ["Location", "Retry-After"];

    public static IServiceCollection AddFrontendCors(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<FrontendCorsOptions>()
            .Bind(configuration.GetSection(FrontendCorsOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<FrontendCorsOptions>, FrontendCorsOptionsValidator>();

        services.AddCors();
        services.AddOptions<CorsOptions>()
            .Configure<IOptions<FrontendCorsOptions>>((cors, frontend) =>
                cors.AddDefaultPolicy(policy => Configure(policy, frontend.Value)));

        return services;
    }

    private static void Configure(CorsPolicyBuilder policy, FrontendCorsOptions options)
    {
        var origins = options.AllowedOrigins
            .Select(origin => origin.Trim().TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        policy
            .WithOrigins(origins)
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials()
            .WithExposedHeaders([.. ExposedHeaders])
            .SetPreflightMaxAge(TimeSpan.FromMinutes(options.PreflightMaxAgeMinutes));
    }
}
