using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Shared.Infrastructure.Email.Persistence;

namespace PetHost.Shared.Infrastructure.Email;

/// <summary>Passos de inicialização da caixa de saída de e-mails: migrations.</summary>
public static class EmailInitializer
{
    /// <param name="applyMigrations">
    /// Em produção isto deve ser <c>false</c>: migration é step dedicado do
    /// pipeline, não roda no startup (§11).
    /// </param>
    public static async Task InitializeEmailAsync(
        this IServiceProvider services,
        bool applyMigrations,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!applyMigrations)
            return;

        var context = await services
            .GetRequiredService<IDbContextFactory<EmailDbContext>>()
            .CreateDbContextAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (context.ConfigureAwait(false))
        {
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
