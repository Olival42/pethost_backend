using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Modules.Conversations.Infrastructure.Persistence.Converters;

namespace PetHost.Modules.Conversations.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="Message"/> na tabela <c>conversation.messages</c>. A FK para
/// <c>conversation.conversations</c> é física, com <c>ON DELETE CASCADE</c>: as duas tabelas são do
/// mesmo módulo (§11 só proíbe entre módulos) e a retenção apaga a conversa inteira.
/// <c>booking_id</c> é FK lógica para <c>booking.bookings.id</c>.
/// </summary>
/// <remarks>
/// Os CHECKs repetem no banco as regras do domínio: texto ou foto, e o lado na lista. O índice
/// parcial das não lidas serve o contador; o de (<c>conversation_id</c>, <c>created_at</c>), a
/// página de mensagens.
/// </remarks>
internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages", table =>
        {
            table.HasCheckConstraint("ck_messages_content", "body IS NOT NULL OR image_url IS NOT NULL OR image_key IS NOT NULL");
            table.HasCheckConstraint(
                "ck_messages_sent_by",
                $"sent_by IN ({string.Join(", ", ConversationPartyValues.All.Select(v => $"'{v}'"))})");
        });

        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id)
            .HasColumnName("id")
            .HasConversion<MessageIdConverter>()
            .ValueGeneratedNever();

        builder.Property(m => m.ConversationId)
            .HasColumnName("conversation_id")
            .HasConversion<ConversationIdConverter>()
            .IsRequired();

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_messages_conversations_conversation_id");

        builder.Property(m => m.SentBy)
            .HasColumnName("sent_by")
            .HasConversion<ConversationPartyConverter>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(m => m.Body).HasColumnName("body").HasMaxLength(Message.BodyMaxLength);
        builder.Property(m => m.ImageUrl).HasColumnName("image_url").HasMaxLength(Message.ImageUrlMaxLength);
        builder.Property(m => m.ImageKey).HasColumnName("image_key").HasMaxLength(Message.ImageKeyMaxLength);
        builder.Property(m => m.BookingId).HasColumnName("booking_id");

        builder.Property(m => m.ReadAt).HasColumnName("read_at").HasColumnType("timestamptz");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt }).HasDatabaseName("ix_messages_conversation_id_created_at");

        builder.HasIndex(m => m.BookingId)
            .HasDatabaseName("ix_messages_booking_id")
            .HasFilter("booking_id IS NOT NULL");

        builder.HasIndex(m => new { m.ConversationId, m.SentBy })
            .HasDatabaseName("ix_messages_conversation_id_sent_by_unread")
            .HasFilter("read_at IS NULL");

        builder.Ignore(m => m.HasPhoto);
        builder.Ignore(m => m.DomainEvents);
    }
}
