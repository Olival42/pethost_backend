using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Pets.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreatePetsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "pet");

            migrationBuilder.CreateTable(
                name: "pets",
                schema: "pet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                    host_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    birth_date = table.Column<DateOnly>(type: "date", nullable: true),
                    breed = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    feeding_notes = table.Column<string>(type: "text", nullable: true),
                    good_with_cats = table.Column<bool>(type: "boolean", nullable: false),
                    good_with_dogs = table.Column<bool>(type: "boolean", nullable: false),
                    good_with_kids = table.Column<bool>(type: "boolean", nullable: false),
                    is_neutered = table.Column<bool>(type: "boolean", nullable: false),
                    is_vaccinated = table.Column<bool>(type: "boolean", nullable: false),
                    medication_notes = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    photo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sex = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    size = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    species = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    species_description = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    vet_contact = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pets", x => x.id);
                    table.CheckConstraint("ck_pets_description_only_for_exotic", "(species = 'exotic') = (species_description IS NOT NULL)");
                    table.CheckConstraint("ck_pets_one_keeper", "num_nonnulls(owner_id, host_id) = 1");
                    table.CheckConstraint("ck_pets_sex", "sex IN ('male', 'female', 'unknown')");
                    table.CheckConstraint("ck_pets_size", "size IS NULL OR size IN ('small', 'medium', 'large')");
                    table.CheckConstraint("ck_pets_size_only_for_dogs", "(species = 'dog') = (size IS NOT NULL)");
                    table.CheckConstraint("ck_pets_species", "species IN ('dog', 'cat', 'cockatiel', 'parrot', 'parakeet', 'canary', 'rabbit', 'hamster', 'guinea_pig', 'fish', 'turtle', 'exotic')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_pets_host_id",
                schema: "pet",
                table: "pets",
                column: "host_id");

            migrationBuilder.CreateIndex(
                name: "ix_pets_owner_id",
                schema: "pet",
                table: "pets",
                column: "owner_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pets",
                schema: "pet");
        }
    }
}
