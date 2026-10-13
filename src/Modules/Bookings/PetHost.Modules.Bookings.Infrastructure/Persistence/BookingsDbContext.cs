using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Bookings.Domain.Bookings;

namespace PetHost.Modules.Bookings.Infrastructure.Persistence;

/// <summary>
/// DbContext do módulo Bookings (reservas). Um contexto e um schema por módulo; nunca sai da
/// Infrastructure (§11).
/// </summary>
public sealed class BookingsDbContext(DbContextOptions<BookingsDbContext> options) : DbContext(options)
{
    /// <summary>Schema isolado do módulo.</summary>
    public const string Schema = "booking";

    /// <summary>Histórico de migrations, isolado no schema do módulo.</summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BookingsDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
