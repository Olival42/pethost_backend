using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Owners.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateOwnersTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "owner");

            migrationBuilder.CreateTable(
                name: "owners",
                schema: "owner",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cpf = table.Column<string>(type: "char(11)", nullable: false),
                    stripe_customer_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_owners", x => x.user_id);
                });

            migrationBuilder.CreateIndex(
                name: "uq_owners_cpf",
                schema: "owner",
                table: "owners",
                column: "cpf",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_owners_stripe_customer_id",
                schema: "owner",
                table: "owners",
                column: "stripe_customer_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "owners",
                schema: "owner");
        }
    }
}
