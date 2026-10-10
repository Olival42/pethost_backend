using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Pets.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowSizeForCats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pets_size_only_for_dogs",
                schema: "pet",
                table: "pets");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pets_size_only_for_dogs_and_cats",
                schema: "pet",
                table: "pets",
                sql: "size IS NULL OR species IN ('dog', 'cat')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pets_size_required_for_dogs",
                schema: "pet",
                table: "pets",
                sql: "species <> 'dog' OR size IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pets_size_only_for_dogs_and_cats",
                schema: "pet",
                table: "pets");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pets_size_required_for_dogs",
                schema: "pet",
                table: "pets");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pets_size_only_for_dogs",
                schema: "pet",
                table: "pets",
                sql: "(species = 'dog') = (size IS NOT NULL)");
        }
    }
}
