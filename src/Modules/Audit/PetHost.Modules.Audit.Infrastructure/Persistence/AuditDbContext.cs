using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Audit.Domain.Entries;

namespace PetHost.Modules.Audit.Infrastructure.Persistence;

/// <summary>
/// DbContext do módulo Audit. Um contexto e um schema por módulo; nunca sai da
/// Infrastructure (§11).
/// </summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    /// <summary>Schema isolado do módulo.</summary>
    public const string Schema = "audit";

    /// <summary>Histórico de migrations, isolado no schema do módulo.</summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuditDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
