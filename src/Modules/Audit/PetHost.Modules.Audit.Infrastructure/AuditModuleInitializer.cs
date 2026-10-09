using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Audit.Infrastructure.Persistence;

namespace PetHost.Modules.Audit.Infrastructure;

/// <summary>Passos de inicialização do módulo Audit: migrations.</summary>
public static class AuditModuleInitializer
{
    /// <param name="applyMigrations">
    /// Em produção isto deve ser <c>false</c>: migration é step dedicado do
    /// pipeline, não roda no startup (§11).
    /// </param>
    public static async Task InitializeAuditModuleAsync(
        this IServiceProvider services,
        bool applyMigrations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!applyMigrations)
            return;

        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<AuditDbContext>()
            .Database
            .MigrateAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
