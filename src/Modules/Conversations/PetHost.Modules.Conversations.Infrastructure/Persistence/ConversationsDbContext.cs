using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;

namespace PetHost.Modules.Conversations.Infrastructure.Persistence;

/// <summary>
/// DbContext do módulo Conversations (chat). Um contexto e um schema por módulo; nunca sai da
/// Infrastructure (§11).
/// </summary>
public sealed class ConversationsDbContext(DbContextOptions<ConversationsDbContext> options) : DbContext(options)
{
    /// <summary>Schema isolado do módulo.</summary>
    public const string Schema = "conversation";

    /// <summary>Histórico de migrations, isolado no schema do módulo.</summary>
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ConversationsDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
