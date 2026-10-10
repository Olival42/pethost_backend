using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Hosts.Domain.Hosts;

namespace PetHost.Modules.Hosts.Infrastructure.Persistence;

/// <summary>
/// DbContext do módulo Hosts. Um contexto e um schema por módulo; nunca sai da
/// Infrastructure (§11).
/// </summary>
public sealed class HostsDbContext(DbContextOptions<HostsDbContext> options) : DbContext(options)
{
    /// <summary>Schema isolado do módulo.</summary>
    public const string Schema = "host";

    /// <summary>Histórico de migrations, isolado no schema do módulo.</summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<Host> Hosts => Set<Host>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(HostsDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
