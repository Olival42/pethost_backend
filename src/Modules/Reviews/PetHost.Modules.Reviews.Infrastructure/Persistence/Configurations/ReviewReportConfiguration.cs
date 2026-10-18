using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Modules.Reviews.Infrastructure.Persistence.Converters;

namespace PetHost.Modules.Reviews.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="ReviewReport"/> na tabela <c>review.review_reports</c>. A FK para
/// <c>reviews</c> é física (mesmo módulo). <c>reported_by</c> e <c>resolved_by</c> são FKs lógicas
/// para <c>auth.users.id</c>.
/// </summary>
/// <remarks>
/// Uma denúncia por conta por avaliação: índice único em (<c>review_id</c>, <c>reported_by</c>).
/// Os CHECKs repetem as listas do domínio e amarram a análise à situação. Concorrência otimista pelo
/// <c>xmin</c>: dois admins analisando a mesma denúncia, o segundo leva <c>409</c>.
/// </remarks>
internal sealed class ReviewReportConfiguration : IEntityTypeConfiguration<ReviewReport>
{
    public void Configure(EntityTypeBuilder<ReviewReport> builder)
    {
        builder.ToTable("review_reports", table =>
        {
            table.HasCheckConstraint("ck_review_reports_reporter_side", $"reporter_side IN ({List(ReviewPartyValues.All)})");
            table.HasCheckConstraint("ck_review_reports_reason", $"reason IN ({List(ReviewReportReasonValues.All)})");
            table.HasCheckConstraint("ck_review_reports_status", $"status IN ({List(ReviewReportStatusValues.All)})");
            table.HasCheckConstraint(
                "ck_review_reports_resolution",
                $"(status = '{ReviewReportStatusValues.Pending}' AND resolved_at IS NULL AND resolved_by IS NULL AND resolution_note IS NULL) " +
                $"OR (status <> '{ReviewReportStatusValues.Pending}' AND resolved_at IS NOT NULL AND resolved_by IS NOT NULL AND resolution_note IS NOT NULL)");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasConversion<ReviewReportIdConverter>()
            .ValueGeneratedNever();

        builder.Property(r => r.ReviewId)
            .HasColumnName("review_id")
            .HasConversion<ReviewIdConverter>()
            .IsRequired();

        builder.HasOne<Review>()
            .WithMany()
            .HasForeignKey(r => r.ReviewId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_review_reports_reviews_review_id");

        builder.Property(r => r.ReportedBy).HasColumnName("reported_by").IsRequired();
        builder.Property(r => r.ReporterSide)
            .HasColumnName("reporter_side")
            .HasConversion<ReviewPartyConverter>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(r => r.Reason)
            .HasColumnName("reason")
            .HasConversion<ReviewReportReasonConverter>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(r => r.Description).HasColumnName("description").HasMaxLength(ReviewReport.TextMaxLength);

        builder.Property(r => r.Status)
            .HasColumnName("status")
            .HasConversion<ReviewReportStatusConverter>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(r => r.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(r => r.ResolvedAt).HasColumnName("resolved_at").HasColumnType("timestamptz");
        builder.Property(r => r.ResolvedBy).HasColumnName("resolved_by");
        builder.Property(r => r.ResolutionNote).HasColumnName("resolution_note").HasMaxLength(ReviewReport.TextMaxLength);

        // xmin: coluna de sistema do Postgres, muda a cada UPDATE. Não entra na migration.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasIndex(r => new { r.ReviewId, r.ReportedBy })
            .IsUnique()
            .HasDatabaseName("uq_review_reports_review_id_reported_by");

        // A fila do admin: por situação, da mais antiga para a mais nova.
        builder.HasIndex(r => new { r.Status, r.CreatedAt }).HasDatabaseName("ix_review_reports_status_created_at");

        builder.Ignore(r => r.IsPending);
        builder.Ignore(r => r.DomainEvents);
    }

    private static string List(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"'{v}'"));
}
