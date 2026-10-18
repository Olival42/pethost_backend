using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Reviews.Infrastructure.Persistence;

namespace PetHost.Modules.Reviews.Infrastructure;

/// <summary>Passos de inicialização do módulo Reviews: migrations.</summary>
public static class ReviewsModuleInitializer
{
    /// <param name="applyMigrations">
    /// Em produção isto deve ser <c>false</c>: migration é step dedicado do
    /// pipeline, não roda no startup (§11).
    /// </param>
    public static async Task InitializeReviewsModuleAsync(
        this IServiceProvider services,
        bool applyMigrations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!applyMigrations)
            return;

        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider
            .GetRequiredService<ReviewsDbContext>()
            .Database
            .MigrateAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
