using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfileAndStatusToUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "birth_date",
                schema: "auth",
                table: "users",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "complement",
                schema: "auth",
                table: "users",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deactivated_at",
                schema: "auth",
                table: "users",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "auth",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "street",
                schema: "auth",
                table: "users",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "street_number",
                schema: "auth",
                table: "users",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "zip_code",
                schema: "auth",
                table: "users",
                type: "char(8)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "birth_date",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "complement",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "deactivated_at",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "street",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "street_number",
                schema: "auth",
                table: "users");

            migrationBuilder.DropColumn(
                name: "zip_code",
                schema: "auth",
                table: "users");
        }
    }
}
