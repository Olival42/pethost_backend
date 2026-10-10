using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Hosts.Infrastructure.Contracts;
using PetHost.Modules.Hosts.Infrastructure.Persistence;
using PetHost.Shared.Contracts.Hosts;

namespace PetHost.Modules.Hosts.Infrastructure;

/// <summary>Registro da camada Infrastructure do módulo Hosts (§3).</summary>
/// <remarks>
/// Por enquanto o módulo tem só a tabela e o contrato de leitura: cadastro, edição e
/// status do anfitrião ainda não existem (sem Application nem Presentation).
/// </remarks>
public static class DependencyInjection
{
    /// <summary>Mesma connection string dos outros módulos: um banco, um schema por módulo.</summary>
    public const string PostgresConnectionName = "Postgres";

    public static IServiceCollection AddHostsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(PostgresConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{PostgresConnectionName}' is not configured.");

        services.AddDbContext<HostsDbContext>(dbOptions => dbOptions
            .UseNpgsql(connectionString, npgsql => npgsql
                .MigrationsHistoryTable(HostsDbContext.MigrationsHistoryTable, HostsDbContext.Schema))
            .UseSnakeCaseNamingConvention());

        // Contrato público de leitura: outros módulos acham o anfitrião só por aqui (§5).
        services.AddScoped<IHostDirectory, HostDirectory>();

        return services;
    }
}
