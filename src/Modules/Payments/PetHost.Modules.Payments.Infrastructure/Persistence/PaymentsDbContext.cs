using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Payments.Domain.Payments;

namespace PetHost.Modules.Payments.Infrastructure.Persistence;

/// <summary>
/// DbContext do módulo Payments (pagamentos). Um contexto e um schema por módulo; nunca sai da
/// Infrastructure (§11).
/// </summary>
public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    /// <summary>Schema isolado do módulo.</summary>
    public const string Schema = "payment";

    /// <summary>Histórico de migrations, isolado no schema do módulo.</summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<Payment> Payments => Set<Payment>();

    /// <summary>Eventos do Stripe já processados: o mesmo evento não é tratado duas vezes.</summary>
    internal DbSet<ProcessedStripeEvent> StripeEvents => Set<ProcessedStripeEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
