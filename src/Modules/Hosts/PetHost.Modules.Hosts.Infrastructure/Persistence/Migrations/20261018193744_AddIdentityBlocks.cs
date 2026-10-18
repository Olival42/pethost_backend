using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Hosts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIdentityBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "identity_blocks",
                schema: "host",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cpf_fingerprint = table.Column<string>(type: "char(64)", nullable: false),
                    reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blocked_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_blocks", x => x.id);
                    table.CheckConstraint("ck_identity_blocks_reason", "reason IN ('fraud', 'attempts_exhausted')");
                });

            migrationBuilder.CreateIndex(
                name: "uq_identity_blocks_cpf_fingerprint",
                schema: "host",
                table: "identity_blocks",
                column: "cpf_fingerprint",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "identity_blocks",
                schema: "host");
        }
    }
}
