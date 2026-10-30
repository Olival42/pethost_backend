using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetHost.Modules.Audit.Domain.Entries;
using PetHost.Modules.Audit.Infrastructure.Persistence.Converters;

namespace PetHost.Modules.Audit.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="AuditEntry"/> na tabela <c>audit.audit_entries</c>. Sem FK para
/// as tabelas dos outros módulos (§5, §11): o registro precisa sobreviver mesmo se a
/// conta ou o tutor forem apagados.
/// </summary>
internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasConversion<AuditEntryIdConverter>()
            .ValueGeneratedNever();

        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(e => e.Action).HasColumnName("action").HasMaxLength(AuditEntry.ActionMaxLength).IsRequired();
        builder.Property(e => e.TargetType).HasColumnName("target_type").HasMaxLength(AuditEntry.TargetTypeMaxLength).IsRequired();
        builder.Property(e => e.TargetId).HasColumnName("target_id");
        builder.Property(e => e.ActorId).HasColumnName("actor_id");
        builder.Property(e => e.ActorRole).HasColumnName("actor_role").HasMaxLength(AuditEntry.ActorRoleMaxLength);
        builder.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(AuditEntry.ReasonMaxLength);
        builder.Property(e => e.IpAddress).HasColumnName("ip_address").HasMaxLength(AuditEntry.IpAddressMaxLength);
        builder.Property(e => e.TraceId).HasColumnName("trace_id").HasMaxLength(AuditEntry.TraceIdMaxLength);

        builder.Property(e => e.Details)
            .HasColumnName("details")
            .HasColumnType("jsonb")
            .HasConversion(new DetailsConverter(), DetailsConverter.Comparer)
            .IsRequired();

        // As consultas do admin: tudo sobre um alvo, tudo de um ator, tudo de um tipo de ação.
        builder.HasIndex(e => new { e.TargetType, e.TargetId, e.OccurredAt }).HasDatabaseName("ix_audit_entries_target");
        builder.HasIndex(e => new { e.ActorId, e.OccurredAt }).HasDatabaseName("ix_audit_entries_actor");
        builder.HasIndex(e => new { e.Action, e.OccurredAt }).HasDatabaseName("ix_audit_entries_action");
        builder.HasIndex(e => e.OccurredAt).HasDatabaseName("ix_audit_entries_occurred_at");

        builder.Ignore(e => e.DomainEvents);
    }
}
