using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Reviews.Domain.Reminders;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Infrastructure.Persistence;

/// <summary>
/// DbContext do módulo Reviews (avaliações). Um contexto e um schema por módulo; nunca sai da
/// Infrastructure (§11).
/// </summary>
public sealed class ReviewsDbContext(DbContextOptions<ReviewsDbContext> options) : DbContext(options)
{
    /// <summary>Schema isolado do módulo.</summary>
    public const string Schema = "review";

    /// <summary>Histórico de migrations, isolado no schema do módulo.</summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<Review> Reviews => Set<Review>();

    public DbSet<ReviewReport> ReviewReports => Set<ReviewReport>();

    public DbSet<ReviewReminder> ReviewReminders => Set<ReviewReminder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReviewsDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
