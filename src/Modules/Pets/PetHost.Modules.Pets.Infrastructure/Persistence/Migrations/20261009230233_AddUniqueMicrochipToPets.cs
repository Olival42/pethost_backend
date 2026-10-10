using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Pets.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueMicrochipToPets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Banco com pets ativos repetindo o microchip (de antes da regra) não aceitaria o
            // índice. O mais antigo fica com o número; os outros só perdem o microchip — nada
            // é desativado nem apagado, e o dono pode informar de novo.
            migrationBuilder.Sql(
                """
                UPDATE pet.pets AS p
                   SET microchip = NULL
                 WHERE p.is_active
                   AND p.microchip IS NOT NULL
                   AND EXISTS (
                       SELECT 1
                         FROM pet.pets AS older
                        WHERE older.is_active
                          AND older.microchip = p.microchip
                          AND (older.created_at, older.id) < (p.created_at, p.id));
                """);

            // Índice parcial em SQL: o EF ainda não cria índice em coluna de tipo complexo.
            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX uq_pets_microchip_active
                    ON pet.pets (microchip)
                    WHERE is_active AND microchip IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX pet.uq_pets_microchip_active;");
        }
    }
}
