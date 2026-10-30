using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetHost.Modules.Hosts.Domain.HostApplications;

namespace PetHost.Modules.Hosts.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="HostApplicationDocument"/> em <c>host.host_application_documents</c>.
/// Guarda a chave do bucket privado, nunca uma URL. Uma foto por vaga (índice único).
/// </summary>
internal sealed class HostApplicationDocumentConfiguration : IEntityTypeConfiguration<HostApplicationDocument>
{
    public void Configure(EntityTypeBuilder<HostApplicationDocument> builder)
    {
        builder.ToTable("host_application_documents", table =>
            table.HasCheckConstraint(
                "ck_host_application_documents_kind",
                $"kind IN ({string.Join(", ", DocumentKindValues.All.Select(v => $"'{v}'"))})"));

        builder.HasKey(d => d.Id);

        builder.Property(d => d.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new HostApplicationDocumentId(value))
            .ValueGeneratedNever();

        builder.Property<HostApplicationId>("ApplicationId")
            .HasColumnName("application_id")
            .HasConversion(id => id.Value, value => new HostApplicationId(value))
            .IsRequired();

        builder.Property(d => d.Kind)
            .HasColumnName("kind")
            .HasConversion(kind => DocumentKindValues.ToWire(kind), value => DocumentKindValues.FromWire(value))
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(d => d.StorageKey)
            .HasColumnName("storage_key")
            .HasMaxLength(HostApplicationDocument.StorageKeyMaxLength)
            .IsRequired();

        builder.Property(d => d.ContentHash)
            .HasColumnName("content_hash")
            .HasColumnType($"char({HostApplicationDocument.ContentHashLength})")
            .IsRequired();

        builder.Property(d => d.SizeBytes).HasColumnName("size_bytes").IsRequired();
        builder.Property(d => d.UploadedAt).HasColumnName("uploaded_at").HasColumnType("timestamptz").IsRequired();

        builder.HasIndex("ApplicationId", nameof(HostApplicationDocument.Kind))
            .HasDatabaseName("uq_host_application_documents_application_id_kind")
            .IsUnique();
    }
}
