using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetHost.Modules.Reviews.Domain.Reminders;
using PetHost.Modules.Reviews.Infrastructure.Persistence.Converters;

namespace PetHost.Modules.Reviews.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="ReviewReminder"/> na tabela <c>review.review_reminders</c>: chave (<c>booking_id</c>,
/// <c>side</c>), um lembrete por lado por reserva. <c>booking_id</c> é FK lógica.
/// </summary>
internal sealed class ReviewReminderConfiguration : IEntityTypeConfiguration<ReviewReminder>
{
    public void Configure(EntityTypeBuilder<ReviewReminder> builder)
    {
        builder.ToTable("review_reminders");

        builder.Property(r => r.BookingId).HasColumnName("booking_id");
        builder.Property(r => r.Side)
            .HasColumnName("side")
            .HasConversion<ReviewPartyConverter>()
            .HasMaxLength(10);

        builder.HasKey(r => new { r.BookingId, r.Side });

        builder.Property(r => r.SentAt).HasColumnName("sent_at").HasColumnType("timestamptz").IsRequired();
    }
}
