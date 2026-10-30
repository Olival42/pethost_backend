using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Hosts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddErasureToHosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "cpf",
                schema: "host",
                table: "hosts",
                type: "char(11)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "char(11)");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "erased_at",
                schema: "host",
                table: "hosts",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_hosts_cpf_only_null_when_erased",
                schema: "host",
                table: "hosts",
                sql: "cpf IS NOT NULL OR erased_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_hosts_cpf_only_null_when_erased",
                schema: "host",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "erased_at",
                schema: "host",
                table: "hosts");

            migrationBuilder.AlterColumn<string>(
                name: "cpf",
                schema: "host",
                table: "hosts",
                type: "char(11)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "char(11)",
                oldNullable: true);
        }
    }
}
