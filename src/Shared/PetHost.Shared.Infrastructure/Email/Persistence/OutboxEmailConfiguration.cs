using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PetHost.Shared.Infrastructure.Email.Persistence;

/// <summary>Mapeia <see cref="OutboxEmail"/> na tabela <c>email.outbox_emails</c>.</summary>
internal sealed class OutboxEmailConfiguration : IEntityTypeConfiguration<OutboxEmail>
{
    public void Configure(EntityTypeBuilder<OutboxEmail> builder)
    {
        builder.ToTable(EmailDbContext.OutboxTable);

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(e => e.ToAddress).HasColumnName("to_address").HasMaxLength(OutboxEmail.AddressMaxLength).IsRequired();
        builder.Property(e => e.ToName).HasColumnName("to_name").HasMaxLength(OutboxEmail.NameMaxLength);
        builder.Property(e => e.Subject).HasColumnName("subject").HasMaxLength(OutboxEmail.SubjectMaxLength).IsRequired();
        builder.Property(e => e.TextBody).HasColumnName("text_body").IsRequired();
        builder.Property(e => e.HtmlBody).HasColumnName("html_body").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(e => e.Attempts).HasColumnName("attempts").IsRequired();
        builder.Property(e => e.NextAttemptAt).HasColumnName("next_attempt_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(e => e.LastError).HasColumnName("last_error").HasMaxLength(OutboxEmail.LastErrorMaxLength);
        builder.Property(e => e.FailedAt).HasColumnName("failed_at").HasColumnType("timestamptz");

        // A busca do despachante: pendentes cuja vez chegou, na ordem.
        builder.HasIndex(e => e.NextAttemptAt)
            .HasDatabaseName("ix_outbox_emails_next_attempt_at")
            .HasFilter("failed_at IS NULL");
    }
}
