using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Listings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateListingsTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "listing");

            migrationBuilder.CreateTable(
                name: "listings",
                schema: "listing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    city = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    complement = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    neighborhood = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    street_number = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    state = table.Column<string>(type: "char(2)", nullable: false),
                    street = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    zip_code = table.Column<string>(type: "char(8)", nullable: false),
                    accepted_species = table.Column<int>(type: "integer", nullable: false),
                    accepts_large = table.Column<bool>(type: "boolean", nullable: false),
                    accepts_medium = table.Column<bool>(type: "boolean", nullable: false),
                    accepts_small = table.Column<bool>(type: "boolean", nullable: false),
                    available_weekdays = table.Column<short>(type: "smallint", nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    has_cats = table.Column<bool>(type: "boolean", nullable: false),
                    has_dogs = table.Column<bool>(type: "boolean", nullable: false),
                    has_kids = table.Column<bool>(type: "boolean", nullable: false),
                    has_yard = table.Column<bool>(type: "boolean", nullable: false),
                    home_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    house_rules = table.Column<string>(type: "text", nullable: true),
                    nightly_rate = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    title = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    latitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    longitude = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listings", x => x.id);
                    table.CheckConstraint("ck_listings_accepted_species", "accepted_species BETWEEN 1 AND 4095");
                    table.CheckConstraint("ck_listings_available_weekdays", "available_weekdays BETWEEN 1 AND 127");
                    table.CheckConstraint("ck_listings_capacity", "capacity BETWEEN 1 AND 200");
                    table.CheckConstraint("ck_listings_home_type", "home_type IS NULL OR home_type IN ('house', 'apartment')");
                    table.CheckConstraint("ck_listings_latitude", "latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_listings_longitude", "longitude BETWEEN -180 AND 180");
                    table.CheckConstraint("ck_listings_nightly_rate", "nightly_rate BETWEEN 10 AND 10000");
                });

            migrationBuilder.CreateTable(
                name: "listing_photos",
                schema: "listing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_hash = table.Column<string>(type: "char(64)", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listing_photos", x => x.id);
                    table.CheckConstraint("ck_listing_photos_position", "position BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "fk_listing_photos_listings",
                        column: x => x.listing_id,
                        principalSchema: "listing",
                        principalTable: "listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_listing_photos_listing_id_content_hash",
                schema: "listing",
                table: "listing_photos",
                columns: new[] { "listing_id", "content_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_listings_host_id",
                schema: "listing",
                table: "listings",
                column: "host_id");

            // Posição única por cantinho, conferida no commit: reordenar e remover renumeram as
            // fotos na mesma transação, trocando posições entre elas.
            migrationBuilder.Sql(
                "ALTER TABLE listing.listing_photos ADD CONSTRAINT uq_listing_photos_listing_id_position " +
                "UNIQUE (listing_id, position) DEFERRABLE INITIALLY DEFERRED;");

            // Busca por raio: a caixa em volta do círculo filtra por latitude/longitude, só dos ativos.
            // O EF não indexa coluna de tipo complexo, por isso em SQL.
            migrationBuilder.Sql(
                "CREATE INDEX ix_listings_latitude_longitude_active ON listing.listings (latitude, longitude) WHERE is_active;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS listing.ix_listings_latitude_longitude_active;");

            migrationBuilder.DropTable(
                name: "listing_photos",
                schema: "listing");

            migrationBuilder.DropTable(
                name: "listings",
                schema: "listing");
        }
    }
}
