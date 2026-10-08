using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Infrastructure.Persistence;

/// <summary>
/// DbContext do módulo Auth. Um contexto e um schema por módulo; nunca sai da
/// Infrastructure (§11).
/// </summary>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    /// <summary>Schema isolado do módulo.</summary>
    public const string Schema = "auth";

    /// <summary>Histórico de migrations, isolado no schema do módulo.</summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuthDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
