using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Listings.Infrastructure.Persistence;

namespace PetHost.Modules.Listings.Infrastructure;

/// <summary>Passos de inicialização do módulo Listings: migrations.</summary>
public static class ListingsModuleInitializer
{
    /// <param name="applyMigrations">
    /// Em produção isto deve ser <c>false</c>: migration é step dedicado do
    /// pipeline, não roda no startup (§11).
    /// </param>
    public static async Task InitializeListingsModuleAsync(
        this IServiceProvider services,
        bool applyMigrations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!applyMigrations)
            return;

        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<ListingsDbContext>()
            .Database
            .MigrateAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
