using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Hosts.Infrastructure.Persistence;

namespace PetHost.Modules.Hosts.Infrastructure;

/// <summary>Passos de inicialização do módulo Hosts: migrations.</summary>
public static class HostsModuleInitializer
{
    /// <param name="applyMigrations">
    /// Em produção isto deve ser <c>false</c>: migration é step dedicado do
    /// pipeline, não roda no startup (§11).
    /// </param>
    public static async Task InitializeHostsModuleAsync(
        this IServiceProvider services,
        bool applyMigrations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!applyMigrations)
            return;

        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<HostsDbContext>()
            .Database
            .MigrateAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
