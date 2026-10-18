using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Reviews.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClosedReviewReportStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_review_reports_resolution",
                schema: "review",
                table: "review_reports");

            migrationBuilder.DropCheckConstraint(
                name: "ck_review_reports_status",
                schema: "review",
                table: "review_reports");

            // Denúncia pendente de avaliação já apagada sai da fila, fechada na hora da exclusão.
            migrationBuilder.Sql(
                "UPDATE review.review_reports rr SET status = 'closed', resolved_at = r.deleted_at " +
                "FROM review.reviews r WHERE rr.review_id = r.id AND rr.status = 'pending' AND r.deleted_at IS NOT NULL;");

            // Denúncia pendente de avaliação já oculta fecha como procedente, com o motivo da ocultação.
            migrationBuilder.Sql(
                "UPDATE review.review_reports rr SET status = 'upheld', resolved_at = r.hidden_at, resolved_by = r.hidden_by, resolution_note = r.hidden_reason " +
                "FROM review.reviews r WHERE rr.review_id = r.id AND rr.status = 'pending' AND r.hidden_at IS NOT NULL;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_review_reports_resolution",
                schema: "review",
                table: "review_reports",
                sql: "(status = 'pending' AND resolved_at IS NULL AND resolved_by IS NULL AND resolution_note IS NULL) OR (status = 'closed' AND resolved_at IS NOT NULL AND resolved_by IS NULL AND resolution_note IS NULL) OR (status IN ('upheld', 'dismissed') AND resolved_at IS NOT NULL AND resolved_by IS NOT NULL AND resolution_note IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_review_reports_status",
                schema: "review",
                table: "review_reports",
                sql: "status IN ('pending', 'upheld', 'dismissed', 'closed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_review_reports_resolution",
                schema: "review",
                table: "review_reports");

            migrationBuilder.DropCheckConstraint(
                name: "ck_review_reports_status",
                schema: "review",
                table: "review_reports");

            // Sem a situação "closed", a denúncia volta a ser pendente.
            migrationBuilder.Sql("UPDATE review.review_reports SET status = 'pending', resolved_at = NULL WHERE status = 'closed';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_review_reports_resolution",
                schema: "review",
                table: "review_reports",
                sql: "(status = 'pending' AND resolved_at IS NULL AND resolved_by IS NULL AND resolution_note IS NULL) OR (status <> 'pending' AND resolved_at IS NOT NULL AND resolved_by IS NOT NULL AND resolution_note IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_review_reports_status",
                schema: "review",
                table: "review_reports",
                sql: "status IN ('pending', 'upheld', 'dismissed')");
        }
    }
}
