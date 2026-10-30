using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Pets.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPetPhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pet_photos",
                schema: "pet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    pet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pet_photos", x => x.id);
                    table.CheckConstraint("ck_pet_photos_position", "position BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "fk_pet_photos_pets",
                        column: x => x.pet_id,
                        principalSchema: "pet",
                        principalTable: "pets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_pet_photos_pet_id_position",
                schema: "pet",
                table: "pet_photos",
                columns: new[] { "pet_id", "position" },
                unique: true);

            // A foto que cada pet já tinha vira a foto 1 (a capa) na tabela nova.
            migrationBuilder.Sql(
                """
                INSERT INTO pet.pet_photos (id, pet_id, url, position, created_at)
                SELECT gen_random_uuid(), id, photo_url, 1, updated_at
                  FROM pet.pets
                 WHERE photo_url IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "photo_url",
                schema: "pet",
                table: "pets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "photo_url",
                schema: "pet",
                table: "pets",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            // Volta só a capa: as outras fotos não cabem na coluna única.
            migrationBuilder.Sql(
                """
                UPDATE pet.pets AS p
                   SET photo_url = ph.url
                  FROM pet.pet_photos AS ph
                 WHERE ph.pet_id = p.id
                   AND ph.position = (SELECT min(position) FROM pet.pet_photos WHERE pet_id = p.id);
                """);

            migrationBuilder.DropTable(
                name: "pet_photos",
                schema: "pet");
        }
    }
}
