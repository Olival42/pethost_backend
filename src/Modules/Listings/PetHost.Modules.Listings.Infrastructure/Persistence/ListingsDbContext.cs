using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Infrastructure.Persistence;

/// <summary>
/// DbContext do módulo Listings (cantinhos). Um contexto e um schema por módulo; nunca sai da
/// Infrastructure (§11).
/// </summary>
public sealed class ListingsDbContext(DbContextOptions<ListingsDbContext> options) : DbContext(options)
{
    /// <summary>Schema isolado do módulo.</summary>
    public const string Schema = "listing";

    /// <summary>Histórico de migrations, isolado no schema do módulo.</summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    /// <summary>Campo de <see cref="Listing"/> com as fotos, para o mapeamento e o <c>Include</c>.</summary>
    internal const string PhotosField = "_photos";

    public DbSet<Listing> Listings => Set<Listing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        // Busca por cidade/bairro sem diferença de acento (unaccent(city) ILIKE unaccent(...)).
        modelBuilder.HasPostgresExtension("unaccent");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ListingsDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
