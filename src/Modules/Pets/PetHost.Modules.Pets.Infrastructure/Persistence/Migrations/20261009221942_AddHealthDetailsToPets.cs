using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Pets.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHealthDetailsToPets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "allergies",
                schema: "pet",
                table: "pets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "microchip",
                schema: "pet",
                table: "pets",
                type: "char(15)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "weight_kg",
                schema: "pet",
                table: "pets",
                type: "numeric(6,3)",
                precision: 6,
                scale: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allergies",
                schema: "pet",
                table: "pets");

            migrationBuilder.DropColumn(
                name: "microchip",
                schema: "pet",
                table: "pets");

            migrationBuilder.DropColumn(
                name: "weight_kg",
                schema: "pet",
                table: "pets");
        }
    }
}
