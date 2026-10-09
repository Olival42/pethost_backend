using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Owners.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSuspensionToOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "cpf",
                schema: "owner",
                table: "owners",
                type: "char(11)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "char(11)");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "suspended_at",
                schema: "owner",
                table: "owners",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_owners_cpf_only_null_when_suspended",
                schema: "owner",
                table: "owners",
                sql: "cpf IS NOT NULL OR suspended_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_owners_cpf_only_null_when_suspended",
                schema: "owner",
                table: "owners");

            migrationBuilder.DropColumn(
                name: "suspended_at",
                schema: "owner",
                table: "owners");

            migrationBuilder.AlterColumn<string>(
                name: "cpf",
                schema: "owner",
                table: "owners",
                type: "char(11)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "char(11)",
                oldNullable: true);
        }
    }
}
