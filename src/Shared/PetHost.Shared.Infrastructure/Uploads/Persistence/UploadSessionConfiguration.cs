using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PetHost.Shared.Infrastructure.Uploads.Persistence;

/// <summary>Mapeia <see cref="UploadSession"/> na tabela <c>uploads.upload_sessions</c>.</summary>
internal sealed class UploadSessionConfiguration : IEntityTypeConfiguration<UploadSession>
{
    public void Configure(EntityTypeBuilder<UploadSession> builder)
    {
        builder.ToTable(UploadsDbContext.SessionsTable);

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(u => u.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(u => u.Role).HasColumnName("role").HasMaxLength(UploadSession.RoleMaxLength).IsRequired();
        builder.Property(u => u.Purpose).HasColumnName("purpose").HasMaxLength(UploadSession.PurposeMaxLength).IsRequired();
        builder.Property(u => u.ResourceId).HasColumnName("resource_id");
        builder.Property(u => u.PhotoId).HasColumnName("photo_id");
        builder.Property(u => u.Kind).HasColumnName("kind").HasMaxLength(UploadSession.KindMaxLength);
        builder.Property(u => u.DeclaredContentType).HasColumnName("declared_content_type").HasMaxLength(UploadSession.ContentTypeMaxLength).IsRequired();
        builder.Property(u => u.DeclaredSizeBytes).HasColumnName("declared_size_bytes").IsRequired();
        builder.Property(u => u.StagingKey).HasColumnName("staging_key").HasMaxLength(UploadSession.StagingKeyMaxLength).IsRequired();
        builder.Property(u => u.Status).HasColumnName("status").HasMaxLength(UploadSession.StatusMaxLength).IsRequired();
        builder.Property(u => u.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(u => u.UploadExpiresAt).HasColumnName("upload_expires_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(u => u.ConfirmExpiresAt).HasColumnName("confirm_expires_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(u => u.LeaseUntil).HasColumnName("lease_until").HasColumnType("timestamptz");
        builder.Property(u => u.CompletedAt).HasColumnName("completed_at").HasColumnType("timestamptz");
        // text, não jsonb: o jsonb reordena as chaves, e a confirmação repetida tem de devolver a resposta igual.
        builder.Property(u => u.ResultJson).HasColumnName("result_json").HasColumnType("text");
        builder.Property(u => u.FailureCode).HasColumnName("failure_code").HasMaxLength(UploadSession.FailureCodeMaxLength);

        builder.HasIndex(u => u.StagingKey).IsUnique().HasDatabaseName("ux_upload_sessions_staging_key");

        // O limite de pendentes por conta.
        builder.HasIndex(u => new { u.UserId, u.Status }).HasDatabaseName("ix_upload_sessions_user_id_status");

        // A limpeza: pendentes vencidos e terminados antigos.
        builder.HasIndex(u => new { u.Status, u.ConfirmExpiresAt }).HasDatabaseName("ix_upload_sessions_status_confirm_expires_at");
    }
}
