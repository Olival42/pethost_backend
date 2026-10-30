using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Audit.Application.Entries;
using PetHost.Modules.Audit.Application.Entries.SearchAuditEntries;
using PetHost.Modules.Audit.Domain.Entries;
using PetHost.Modules.Audit.Infrastructure.Persistence;
using PetHost.Modules.Audit.Infrastructure.Persistence.Repositories;
using PetHost.Modules.Audit.Infrastructure.Trail;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Audit.Infrastructure;

/// <summary>Registro da camada Infrastructure do módulo Audit (§3).</summary>
public static class DependencyInjection
{
    /// <summary>Mesma connection string dos outros módulos: um banco, um schema por módulo.</summary>
    public const string PostgresConnectionName = "Postgres";

    public static IServiceCollection AddAuditInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(PostgresConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{PostgresConnectionName}' is not configured.");

        // Factory: cada registro da trilha grava num contexto próprio (ver AuditTrail).
        services.AddDbContextFactory<AuditDbContext>(dbOptions => dbOptions
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable(AuditDbContext.MigrationsHistoryTable, AuditDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        services.AddScoped(provider =>
            provider.GetRequiredService<IDbContextFactory<AuditDbContext>>().CreateDbContext());

        services.AddHttpContextAccessor();

        services.AddScoped<IAuditEntryRepository, AuditEntryRepository>();

        // Contrato público: todo módulo registra na trilha por aqui (§5).
        services.AddScoped<IAuditTrail, AuditTrail>();

        services.AddScoped<
            IQueryHandler<SearchAuditEntriesQuery, PagedResult<AuditEntryResponse>>,
            SearchAuditEntriesQueryHandler>();

        return services;
    }
}
