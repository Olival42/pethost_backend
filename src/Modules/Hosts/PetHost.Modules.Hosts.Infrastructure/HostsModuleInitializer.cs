using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Hosts.Infrastructure.Persistence;
using PetHost.Modules.Hosts.Infrastructure.Persistence.Seed;

namespace PetHost.Modules.Hosts.Infrastructure;

/// <summary>Passos de inicialização do módulo Hosts: migrations e o anfitrião de demonstração.</summary>
public static class HostsModuleInitializer
{
    /// <param name="applyMigrations">
    /// Em produção isto deve ser <c>false</c>: migration é step dedicado do
    /// pipeline, não roda no startup (§11).
    /// </param>
    /// <remarks>
    /// Rode depois do <c>InitializeAuthModuleAsync</c>: o seed de demonstração cria a conta
    /// pelo Auth.
    /// </remarks>
    public static async Task InitializeHostsModuleAsync(
        this IServiceProvider services,
        bool applyMigrations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();

        if (applyMigrations)
        {
            await scope.ServiceProvider
                .GetRequiredService<HostsDbContext>()
                .Database
                .MigrateAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        // Idempotente e desligado por padrão (Seed:DemoHost:Enabled).
        await scope.ServiceProvider
            .GetRequiredService<HostsDbSeeder>()
            .SeedAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
