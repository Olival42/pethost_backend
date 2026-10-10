using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Pets.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContentHashToPetPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "content_hash",
                schema: "pet",
                table: "pet_photos",
                type: "char(64)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_pet_photos_pet_id_content_hash",
                schema: "pet",
                table: "pet_photos",
                columns: new[] { "pet_id", "content_hash" },
                unique: true,
                filter: "content_hash IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_pet_photos_pet_id_content_hash",
                schema: "pet",
                table: "pet_photos");

            migrationBuilder.DropColumn(
                name: "content_hash",
                schema: "pet",
                table: "pet_photos");
        }
    }
}
