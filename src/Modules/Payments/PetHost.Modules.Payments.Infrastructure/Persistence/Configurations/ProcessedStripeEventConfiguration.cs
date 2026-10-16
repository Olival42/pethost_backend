using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PetHost.Modules.Payments.Infrastructure.Persistence.Configurations;

/// <summary>Mapeia <see cref="ProcessedStripeEvent"/> em <c>payment.stripe_events</c>.</summary>
internal sealed class ProcessedStripeEventConfiguration : IEntityTypeConfiguration<ProcessedStripeEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedStripeEvent> builder)
    {
        builder.ToTable("stripe_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(e => e.StripeEventId).HasColumnName("stripe_event_id").HasMaxLength(ProcessedStripeEvent.StripeEventIdMaxLength).IsRequired();
        builder.Property(e => e.Type).HasColumnName("type").HasMaxLength(ProcessedStripeEvent.TypeMaxLength).IsRequired();
        builder.Property(e => e.ProcessedAt).HasColumnName("processed_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex(e => e.StripeEventId).IsUnique().HasDatabaseName("uq_stripe_events_stripe_event_id");
    }
}
