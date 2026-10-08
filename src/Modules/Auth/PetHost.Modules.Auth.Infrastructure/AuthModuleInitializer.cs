using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Auth.Infrastructure.Persistence;
using PetHost.Modules.Auth.Infrastructure.Persistence.Seed;

namespace PetHost.Modules.Auth.Infrastructure;

/// <summary>Passos de inicialização do módulo Auth: migrations e seed.</summary>
public static class AuthModuleInitializer
{
    /// <summary>
    /// Aplica migrations (quando pedido) e roda o seed do admin.
    /// </summary>
    /// <param name="applyMigrations">
    /// Em produção isto deve ser <c>false</c>: migration é step dedicado do
    /// pipeline, não roda no startup (§11).
    /// </param>
    public static async Task InitializeAuthModuleAsync(
        this IServiceProvider services,
        bool applyMigrations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();

        if (applyMigrations)
        {
            await scope.ServiceProvider
                .GetRequiredService<AuthDbContext>()
                .Database
                .MigrateAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        await scope.ServiceProvider
            .GetRequiredService<AuthDbSeeder>()
            .SeedAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
