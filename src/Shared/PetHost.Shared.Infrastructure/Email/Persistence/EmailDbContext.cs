using Microsoft.EntityFrameworkCore;

namespace PetHost.Shared.Infrastructure.Email.Persistence;

/// <summary>
/// DbContext da caixa de saída de e-mails. Schema próprio, como cada módulo (§11): a fila é
/// infraestrutura compartilhada e não pertence a nenhum módulo.
/// </summary>
public sealed class EmailDbContext(DbContextOptions<EmailDbContext> options) : DbContext(options)
{
    public const string Schema = "email";

    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public const string OutboxTable = "outbox_emails";

    public DbSet<OutboxEmail> Outbox => Set<OutboxEmail>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new OutboxEmailConfiguration());

        base.OnModelCreating(modelBuilder);
    }
}
