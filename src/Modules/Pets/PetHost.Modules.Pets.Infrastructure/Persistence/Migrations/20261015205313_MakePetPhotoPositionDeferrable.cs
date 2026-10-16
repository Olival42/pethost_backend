using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Pets.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MakePetPhotoPositionDeferrable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_pet_photos_pet_id_position",
                schema: "pet",
                table: "pet_photos");

            // Vaga única por pet, conferida no commit: reordenar troca vagas entre fotos na mesma
            // transação. O EF não cria constraint deferrable, por isso em SQL (como nas fotos do cantinho).
            migrationBuilder.Sql(
                "ALTER TABLE pet.pet_photos ADD CONSTRAINT uq_pet_photos_pet_id_position " +
                "UNIQUE (pet_id, position) DEFERRABLE INITIALLY DEFERRED;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE pet.pet_photos DROP CONSTRAINT uq_pet_photos_pet_id_position;");

            migrationBuilder.CreateIndex(
                name: "uq_pet_photos_pet_id_position",
                schema: "pet",
                table: "pet_photos",
                columns: new[] { "pet_id", "position" },
                unique: true);
        }
    }
}
