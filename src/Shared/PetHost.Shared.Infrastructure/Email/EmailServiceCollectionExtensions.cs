using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PetHost.Shared.Infrastructure.Email.Persistence;

namespace PetHost.Shared.Infrastructure.Email;

/// <summary>Registro do envio de e-mail: caixa de saída no Postgres, despachante em segundo plano e SMTP.</summary>
public static class EmailServiceCollectionExtensions
{
    /// <summary>Mesma connection string dos módulos: um banco, a fila no schema <c>email</c>.</summary>
    public const string PostgresConnectionName = "Postgres";

    public static IServiceCollection AddEmail(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>();

        var connectionString = configuration.GetConnectionString(PostgresConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{PostgresConnectionName}' is not configured.");

        // Factory: quem enfileira e o despachante abrem um contexto curto por operação.
        services.AddDbContextFactory<EmailDbContext>(dbOptions => dbOptions
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable(EmailDbContext.MigrationsHistoryTable, EmailDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddSingleton<EmailOutbox>();
        services.AddSingleton<IEmailOutbox>(provider => provider.GetRequiredService<EmailOutbox>());
        services.AddSingleton<IEmailSender, SmtpEmailSender>();
        services.AddHostedService<EmailDispatcher>();

        return services;
    }
}
