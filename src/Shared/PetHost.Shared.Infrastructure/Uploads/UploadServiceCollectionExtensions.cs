using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Shared.Infrastructure.Cors;
using PetHost.Shared.Infrastructure.Email;
using PetHost.Shared.Infrastructure.Uploads.Persistence;

namespace PetHost.Shared.Infrastructure.Uploads;

/// <summary>
/// Registro do upload direto ao bucket: <c>/api/v1/uploads</c>, a tabela no schema
/// <c>uploads</c> e a limpeza em segundo plano. Os módulos registram as finalidades
/// (<see cref="IUploadPurpose"/>).
/// </summary>
public static partial class UploadServiceCollectionExtensions
{
    /// <summary>Precisa de <c>AddImageStorage</c> (mesmo bucket privado e credenciais).</summary>
    public static IServiceCollection AddDirectUploads(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<UploadOptions>()
            .Bind(configuration.GetSection(UploadOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<UploadOptions>, UploadOptionsValidator>();

        var connectionString = configuration.GetConnectionString(EmailServiceCollectionExtensions.PostgresConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{EmailServiceCollectionExtensions.PostgresConnectionName}' is not configured.");

        services.AddDbContextFactory<UploadsDbContext>(dbOptions => dbOptions
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable(UploadsDbContext.MigrationsHistoryTable, UploadsDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddSingleton<UploadStaging>();
        services.AddScoped<UploadService>();
        services.AddSingleton<UploadCleanupService>();
        services.AddHostedService(provider => provider.GetRequiredService<UploadCleanupService>());

        return services;
    }

    /// <summary>Registra o controller (<c>/api/v1/uploads</c>) no MVC.</summary>
    public static IMvcBuilder AddDirectUploadsEndpoints(this IMvcBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddApplicationPart(typeof(UploadServiceCollectionExtensions).Assembly);
    }

    /// <summary>
    /// Migrations (fora de produção) e, se <see cref="UploadOptions.ConfigureBucketCors"/>, o CORS
    /// do bucket privado. Falha no CORS não derruba a subida: vai para o log.
    /// </summary>
    public static async Task InitializeDirectUploadsAsync(
        this IServiceProvider services,
        bool applyMigrations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (applyMigrations)
        {
            var context = await services
                .GetRequiredService<IDbContextFactory<UploadsDbContext>>()
                .CreateDbContextAsync(cancellationToken)
                .ConfigureAwait(false);

            await using (context.ConfigureAwait(false))
            {
                await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        if (!services.GetRequiredService<IOptions<UploadOptions>>().Value.ConfigureBucketCors)
            return;

        var origins = services.GetRequiredService<IOptions<FrontendCorsOptions>>().Value.AllowedOrigins
            .Select(origin => origin.Trim().TrimEnd('/'))
            .Where(origin => origin.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(UploadServiceCollectionExtensions));
        if (origins.Count == 0)
        {
            LogNoOrigins(logger);
            return;
        }

        try
        {
            await services.GetRequiredService<UploadStaging>().ConfigureCorsAsync(origins, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Sem CORS o navegador não envia, mas a API continua de pé: o log diz o que fazer.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogCorsFailed(logger, exception);
        }
    }

    [LoggerMessage(EventId = 6022, Level = LogLevel.Warning, Message = "Uploads:ConfigureBucketCors is on but Cors:AllowedOrigins is empty; bucket CORS not set.")]
    private static partial void LogNoOrigins(ILogger logger);

    [LoggerMessage(EventId = 6023, Level = LogLevel.Warning, Message = "Could not set the private bucket CORS; configure it in the storage provider panel.")]
    private static partial void LogCorsFailed(ILogger logger, Exception exception);
}
