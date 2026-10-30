using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Owners.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddErasedAtToOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_owners_cpf_only_null_when_suspended",
                schema: "owner",
                table: "owners");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "erased_at",
                schema: "owner",
                table: "owners",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_owners_cpf_only_null_when_suspended_or_erased",
                schema: "owner",
                table: "owners",
                sql: "cpf IS NOT NULL OR suspended_at IS NOT NULL OR erased_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_owners_cpf_only_null_when_suspended_or_erased",
                schema: "owner",
                table: "owners");

            migrationBuilder.DropColumn(
                name: "erased_at",
                schema: "owner",
                table: "owners");

            migrationBuilder.AddCheckConstraint(
                name: "ck_owners_cpf_only_null_when_suspended",
                schema: "owner",
                table: "owners",
                sql: "cpf IS NOT NULL OR suspended_at IS NOT NULL");
        }
    }
}
