using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Shared.Infrastructure.Uploads.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateUploadSessionsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "uploads");

            migrationBuilder.CreateTable(
                name: "upload_sessions",
                schema: "uploads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    photo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    declared_content_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    declared_size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    staging_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    upload_expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    confirm_expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    lease_until = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    result_json = table.Column<string>(type: "text", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_upload_sessions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_upload_sessions_status_confirm_expires_at",
                schema: "uploads",
                table: "upload_sessions",
                columns: new[] { "status", "confirm_expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_upload_sessions_user_id_status",
                schema: "uploads",
                table: "upload_sessions",
                columns: new[] { "user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_upload_sessions_staging_key",
                schema: "uploads",
                table: "upload_sessions",
                column: "staging_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "upload_sessions",
                schema: "uploads");
        }
    }
}
