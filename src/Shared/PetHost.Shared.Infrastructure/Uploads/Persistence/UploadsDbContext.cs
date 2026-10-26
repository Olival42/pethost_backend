using Microsoft.EntityFrameworkCore;

namespace PetHost.Shared.Infrastructure.Uploads.Persistence;

/// <summary>
/// DbContext dos uploads diretos ao bucket. Schema próprio, como a caixa de saída de e-mails:
/// é infraestrutura compartilhada, não pertence a nenhum módulo.
/// </summary>
public sealed class UploadsDbContext(DbContextOptions<UploadsDbContext> options) : DbContext(options)
{
    public const string Schema = "uploads";

    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public const string SessionsTable = "upload_sessions";

    public DbSet<UploadSession> Sessions => Set<UploadSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new UploadSessionConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
