using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Modules.Conversations.Domain.Reports;
using PetHost.Modules.Conversations.Infrastructure.Persistence.Converters;

namespace PetHost.Modules.Conversations.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="MessageReport"/> na tabela <c>conversation.message_reports</c>. As FKs para
/// <c>messages</c> e <c>conversations</c> são físicas, com <c>ON DELETE CASCADE</c> (mesmo módulo):
/// quando a retenção apaga a conversa, as denúncias já analisadas vão junto. <c>resolved_by</c> é
/// FK lógica para <c>auth.users.id</c> (a conta do admin).
/// </summary>
/// <remarks>
/// Uma denúncia por mensagem: índice único em <c>message_id</c>. Os CHECKs repetem as listas do
/// domínio e amarram a análise à situação (pendente sem data de análise; analisada com data, admin
/// e observação). Concorrência otimista pelo <c>xmin</c>: dois admins analisando a mesma denúncia ao
/// mesmo tempo, o segundo leva <c>409</c>.
/// </remarks>
internal sealed class MessageReportConfiguration : IEntityTypeConfiguration<MessageReport>
{
    public void Configure(EntityTypeBuilder<MessageReport> builder)
    {
        builder.ToTable("message_reports", table =>
        {
            table.HasCheckConstraint("ck_message_reports_reported_by", $"reported_by IN ({List(ConversationPartyValues.All)})");
            table.HasCheckConstraint("ck_message_reports_reason", $"reason IN ({List(ReportReasonValues.All)})");
            table.HasCheckConstraint("ck_message_reports_status", $"status IN ({List(ReportStatusValues.All)})");
            table.HasCheckConstraint(
                "ck_message_reports_resolution",
                $"(status = '{ReportStatusValues.Pending}' AND resolved_at IS NULL AND resolved_by IS NULL AND resolution_note IS NULL) " +
                $"OR (status <> '{ReportStatusValues.Pending}' AND resolved_at IS NOT NULL AND resolved_by IS NOT NULL AND resolution_note IS NOT NULL)");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasConversion<MessageReportIdConverter>()
            .ValueGeneratedNever();

        builder.Property(r => r.MessageId)
            .HasColumnName("message_id")
            .HasConversion<MessageIdConverter>()
            .IsRequired();

        builder.HasOne<Message>()
            .WithMany()
            .HasForeignKey(r => r.MessageId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_message_reports_messages_message_id");

        builder.Property(r => r.ConversationId)
            .HasColumnName("conversation_id")
            .HasConversion<ConversationIdConverter>()
            .IsRequired();

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(r => r.ConversationId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_message_reports_conversations_conversation_id");

        builder.Property(r => r.ReportedBy)
            .HasColumnName("reported_by")
            .HasConversion<ConversationPartyConverter>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(r => r.Reason)
            .HasColumnName("reason")
            .HasConversion<ReportReasonConverter>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.Description).HasColumnName("description").HasMaxLength(MessageReport.DescriptionMaxLength);

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasConversion<ReportStatusConverter>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.ResolvedAt).HasColumnName("resolved_at").HasColumnType("timestamptz");
        builder.Property(r => r.ResolvedBy).HasColumnName("resolved_by");
        builder.Property(r => r.ResolutionNote).HasColumnName("resolution_note").HasMaxLength(MessageReport.ResolutionNoteMaxLength);

        // xmin: coluna de sistema do Postgres, muda a cada UPDATE. Não entra na migration.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasIndex(r => r.MessageId)
            .IsUnique()
            .HasDatabaseName("uq_message_reports_message_id");

        // A fila do admin: por situação, da mais antiga para a mais nova.
        builder.HasIndex(r => new { r.Status, r.CreatedAt }).HasDatabaseName("ix_message_reports_status_created_at");

        // A retenção pergunta se a conversa tem denúncia pendente.
        builder.HasIndex(r => r.ConversationId)
            .HasDatabaseName("ix_message_reports_conversation_id_pending")
            .HasFilter($"status = '{ReportStatusValues.Pending}'");

        builder.Ignore(r => r.IsPending);
        builder.Ignore(r => r.DomainEvents);
    }

    private static string List(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"'{v}'"));
}
