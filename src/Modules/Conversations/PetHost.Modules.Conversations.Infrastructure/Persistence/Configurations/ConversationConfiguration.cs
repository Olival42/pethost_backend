using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Infrastructure.Persistence.Converters;

namespace PetHost.Modules.Conversations.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="Conversation"/> na tabela <c>conversation.conversations</c>. <c>owner_id</c> e
/// <c>host_id</c> são <b>FKs lógicas</b> (<c>owner.owners.id</c>, <c>host.hosts.id</c>), sem
/// constraint física: cada módulo tem o seu schema e não há restrição nem JOIN entre schemas (§5, §11).
/// </summary>
/// <remarks>
/// Uma conversa por dupla: índice único em (<c>owner_id</c>, <c>host_id</c>). Sem <c>xmin</c>: a
/// conversa só muda o instante da última mensagem, que só avança; com a concorrência otimista, duas
/// mensagens enviadas ao mesmo tempo virariam um <c>409</c> para uma delas. O bloqueio
/// de um dos lados (<c>blocked_by</c>, <c>blocked_at</c>) e o da moderação (<c>moderation_blocked_at</c>)
/// gravam dentro da mesma trava do envio.
/// </remarks>
internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations", table =>
        {
            table.HasCheckConstraint(
                "ck_conversations_blocked_by",
                $"blocked_by IS NULL OR blocked_by IN ({string.Join(", ", ConversationPartyValues.All.Select(v => $"'{v}'"))})");
            table.HasCheckConstraint("ck_conversations_block", "(blocked_by IS NULL) = (blocked_at IS NULL)");
        });

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasConversion<ConversationIdConverter>()
            .ValueGeneratedNever();

        builder.Property(c => c.OwnerId).HasColumnName("owner_id").IsRequired();
        builder.Property(c => c.HostId).HasColumnName("host_id").IsRequired();

        builder.Property(c => c.LastMessageAt).HasColumnName("last_message_at").HasColumnType("timestamptz");
        builder.Property(c => c.BlockedBy)
            .HasColumnName("blocked_by")
            .HasConversion<ConversationPartyConverter>()
            .HasMaxLength(10);

        builder.Property(c => c.BlockedAt).HasColumnName("blocked_at").HasColumnType("timestamptz");
        builder.Property(c => c.ModerationBlockedAt).HasColumnName("moderation_blocked_at").HasColumnType("timestamptz");
        builder.Property(c => c.OwnerUnreadNotifiedAt).HasColumnName("owner_unread_notified_at").HasColumnType("timestamptz");
        builder.Property(c => c.HostUnreadNotifiedAt).HasColumnName("host_unread_notified_at").HasColumnType("timestamptz");

        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(c => new { c.OwnerId, c.HostId })
            .IsUnique()
            .HasDatabaseName("uq_conversations_owner_id_host_id");

        // Telas "Conversas" do tutor e do anfitrião, da atividade mais recente para a mais antiga.
        builder.HasIndex(c => new { c.OwnerId, c.LastMessageAt }).HasDatabaseName("ix_conversations_owner_id_last_message_at");
        builder.HasIndex(c => new { c.HostId, c.LastMessageAt }).HasDatabaseName("ix_conversations_host_id_last_message_at");

        builder.Ignore(c => c.LastActivityAt);
        builder.Ignore(c => c.IsBlocked);
        builder.Ignore(c => c.IsBlockedByModeration);
        builder.Ignore(c => c.DomainEvents);
    }
}
