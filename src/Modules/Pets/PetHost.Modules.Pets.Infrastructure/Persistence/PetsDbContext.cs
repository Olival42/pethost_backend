using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Pets.Domain.Pets;

namespace PetHost.Modules.Pets.Infrastructure.Persistence;

/// <summary>
/// DbContext do módulo Pets. Um contexto e um schema por módulo; nunca sai da
/// Infrastructure (§11).
/// </summary>
public sealed class PetsDbContext(DbContextOptions<PetsDbContext> options) : DbContext(options)
{
    /// <summary>Schema isolado do módulo.</summary>
    public const string Schema = "pet";

    /// <summary>Histórico de migrations, isolado no schema do módulo.</summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>Campo de <see cref="Pet"/> com as fotos, para o mapeamento e o <c>Include</c>.</summary>
    internal const string PhotosField = "_photos";

    public DbSet<Pet> Pets => Set<Pet>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PetsDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
