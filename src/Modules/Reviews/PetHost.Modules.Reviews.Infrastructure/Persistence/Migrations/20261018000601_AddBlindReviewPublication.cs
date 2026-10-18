using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Reviews.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBlindReviewPublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "published_at",
                schema: "review",
                table: "reviews",
                type: "timestamptz",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "subject_notified_at",
                schema: "review",
                table: "reviews",
                type: "timestamptz",
                nullable: true);

            // Até aqui a avaliação aparecia na hora e o avaliado já era avisado: as que existem
            // contam como publicadas e avisadas quando foram criadas.
            migrationBuilder.Sql("UPDATE review.reviews SET published_at = created_at, subject_notified_at = created_at;");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_published_at_to_notify",
                schema: "review",
                table: "reviews",
                column: "published_at",
                filter: "subject_notified_at IS NULL AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_reviews_published_at_to_notify",
                schema: "review",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "published_at",
                schema: "review",
                table: "reviews");

            migrationBuilder.DropColumn(
                name: "subject_notified_at",
                schema: "review",
                table: "reviews");
        }
    }
}
