using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Reviews.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewSidesRepliesModerationAndReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reviews_host_id_created_at",
                schema: "review",
                table: "reviews");

            migrationBuilder.DropIndex(
                name: "ix_reviews_listing_id_created_at",
                schema: "review",
                table: "reviews");

            migrationBuilder.DropIndex(
                name: "ix_reviews_owner_id_created_at",
                schema: "review",
                table: "reviews");

            migrationBuilder.DropIndex(
                name: "uq_reviews_booking_id",
                schema: "review",
                table: "reviews");

            migrationBuilder.AddColumn<string>(
                name: "author_side",
                schema: "review",
                table: "reviews",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "owner"); // Até aqui só o tutor avaliava: as linhas que já existem são dele.

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "hidden_at",
                schema: "review",
                table: "reviews",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "hidden_by",
                schema: "review",
                table: "reviews",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hidden_reason",
                schema: "review",
                table: "reviews",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "replied_at",
                schema: "review",
                table: "reviews",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reply_text",
                schema: "review",
                table: "reviews",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "review_pet_ratings",
                schema: "review",
                columns: table => new
                {
                    pet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating = table.Column<short>(type: "smallint", nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_pet_ratings", x => new { x.review_id, x.pet_id });
                    table.CheckConstraint("ck_review_pet_ratings_rating", "rating BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_review_pet_ratings_reviews_review_id",
                        column: x => x.review_id,
                        principalSchema: "review",
                        principalTable: "reviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "review_reminders",
                schema: "review",
                columns: table => new
                {
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    side = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_reminders", x => new { x.booking_id, x.side });
                });

            migrationBuilder.CreateTable(
                name: "review_reports",
                schema: "review",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    review_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_side = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_review_reports", x => x.id);
                    table.CheckConstraint("ck_review_reports_reason", "reason IN ('offensive', 'false_information', 'personal_data', 'spam', 'other')");
                    table.CheckConstraint("ck_review_reports_reporter_side", "reporter_side IN ('owner', 'host')");
                    table.CheckConstraint("ck_review_reports_resolution", "(status = 'pending' AND resolved_at IS NULL AND resolved_by IS NULL AND resolution_note IS NULL) OR (status <> 'pending' AND resolved_at IS NOT NULL AND resolved_by IS NOT NULL AND resolution_note IS NOT NULL)");
                    table.CheckConstraint("ck_review_reports_status", "status IN ('pending', 'upheld', 'dismissed')");
                    table.ForeignKey(
                        name: "fk_review_reports_reviews_review_id",
                        column: x => x.review_id,
                        principalSchema: "review",
                        principalTable: "reviews",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_host_id_author_side_created_at",
                schema: "review",
                table: "reviews",
                columns: new[] { "host_id", "author_side", "created_at" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_listing_id_author_side_created_at",
                schema: "review",
                table: "reviews",
                columns: new[] { "listing_id", "author_side", "created_at" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_owner_id_author_side_created_at",
                schema: "review",
                table: "reviews",
                columns: new[] { "owner_id", "author_side", "created_at" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_reviews_booking_id_author_side",
                schema: "review",
                table: "reviews",
                columns: new[] { "booking_id", "author_side" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_reviews_author_side",
                schema: "review",
                table: "reviews",
                sql: "author_side IN ('owner', 'host')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reviews_hidden",
                schema: "review",
                table: "reviews",
                sql: "(hidden_at IS NULL) = (hidden_by IS NULL) AND (hidden_at IS NULL) = (hidden_reason IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_reviews_reply",
                schema: "review",
                table: "reviews",
                sql: "(reply_text IS NULL) = (replied_at IS NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_review_pet_ratings_pet_id",
                schema: "review",
                table: "review_pet_ratings",
                column: "pet_id");

            migrationBuilder.CreateIndex(
                name: "ix_review_reports_status_created_at",
                schema: "review",
                table: "review_reports",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "uq_review_reports_review_id_reported_by",
                schema: "review",
                table: "review_reports",
                columns: new[] { "review_id", "reported_by" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "review_pet_ratings",
                schema: "review");

            migrationBuilder.DropTable(
                name: "review_reminders",
                schema: "review");

            migrationBuilder.DropTable(
                name: "review_reports",
                schema: "review");

            migrationBuilder.DropIndex(
                name: "ix_reviews_host_id_author_side_created_at",
                schema: "review",
                table: "reviews");

            migrationBuilder.DropIndex(
                name: "ix_reviews_listing_id_author_side_created_at",
                schema: "review",
                table: "reviews");

            migrationBuilder.DropIndex(
                name: "ix_reviews_owner_id_author_side_created_at",
                schema: "review",
                table: "reviews");

            migrationBuilder.DropIndex(
                name: "uq_reviews_booking_id_author_side",
                schema: "review",
                table: "reviews");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reviews_author_side",
                schema: "review",
                table: "reviews");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reviews_hidden",
                schema: "review",
                table: "reviews");

            migrationBuilder.DropCheckConstraint(
                name: "ck_reviews_reply",
                schema: "review",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "author_side",
                schema: "review",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "hidden_at",
                schema: "review",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "hidden_by",
                schema: "review",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "hidden_reason",
                schema: "review",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "replied_at",
                schema: "review",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "reply_text",
                schema: "review",
                table: "reviews");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_host_id_created_at",
                schema: "review",
                table: "reviews",
                columns: new[] { "host_id", "created_at" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_listing_id_created_at",
                schema: "review",
                table: "reviews",
                columns: new[] { "listing_id", "created_at" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_owner_id_created_at",
                schema: "review",
                table: "reviews",
                columns: new[] { "owner_id", "created_at" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_reviews_booking_id",
                schema: "review",
                table: "reviews",
                column: "booking_id",
                unique: true);
        }
    }
}
