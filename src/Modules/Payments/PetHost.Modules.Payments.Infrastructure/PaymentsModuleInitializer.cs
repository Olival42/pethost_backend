using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Payments.Infrastructure.Persistence;

namespace PetHost.Modules.Payments.Infrastructure;

/// <summary>Passos de inicialização do módulo Payments: migrations.</summary>
public static class PaymentsModuleInitializer
{
    /// <param name="applyMigrations">
    /// Em produção isto deve ser <c>false</c>: migration é step dedicado do
    /// pipeline, não roda no startup (§11).
    /// </param>
    public static async Task InitializePaymentsModuleAsync(
        this IServiceProvider services,
        bool applyMigrations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!applyMigrations)
            return;

        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<PaymentsDbContext>()
            .Database
            .MigrateAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
