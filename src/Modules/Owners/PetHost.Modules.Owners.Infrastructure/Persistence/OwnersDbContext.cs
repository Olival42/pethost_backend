using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Owners.Domain.Owners;

namespace PetHost.Modules.Owners.Infrastructure.Persistence;

/// <summary>
/// DbContext do módulo Owners. Um contexto e um schema por módulo; nunca sai da
/// Infrastructure (§11).
/// </summary>
public sealed class OwnersDbContext(DbContextOptions<OwnersDbContext> options) : DbContext(options)
{
    /// <summary>Schema isolado do módulo.</summary>
    public const string Schema = "owner";

    /// <summary>Histórico de migrations, isolado no schema do módulo.</summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<Owner> Owners => Set<Owner>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OwnersDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
