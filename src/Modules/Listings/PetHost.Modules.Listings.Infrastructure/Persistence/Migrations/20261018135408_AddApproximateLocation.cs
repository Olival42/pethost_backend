using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Listings.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Ponto aproximado do cantinho para o mapa público. Os cantinhos que já existem ganham um
    /// sorteio igual ao do domínio (<c>GeoPoint.Approximate</c>): até 0,4 km em qualquer direção,
    /// arredondado para 3 casas. A busca por raio passa a usar esse ponto, então o índice muda junto.
    /// </summary>
    public partial class AddApproximateLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "approx_latitude",
                schema: "listing",
                table: "listings",
                type: "numeric(6,3)",
                precision: 6,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "approx_longitude",
                schema: "listing",
                table: "listings",
                type: "numeric(6,3)",
                precision: 6,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            // sqrt(random()) espalha por igual no disco; 111.32 km por grau.
            migrationBuilder.Sql(
                """
                UPDATE listing.listings AS l
                SET approx_latitude = round(GREATEST(-90, LEAST(90,
                        l.latitude::float8 + (o.km * cos(o.bearing) / 111.32)))::numeric, 3),
                    approx_longitude = round(GREATEST(-180, LEAST(180,
                        l.longitude::float8 + (o.km * sin(o.bearing) / (111.32 * GREATEST(cos(radians(l.latitude::float8)), 0.01)))))::numeric, 3)
                FROM (SELECT id, 0.4 * sqrt(random()) AS km, 2 * pi() * random() AS bearing FROM listing.listings) AS o
                WHERE o.id = l.id;
                """);

            migrationBuilder.Sql("DROP INDEX IF EXISTS listing.ix_listings_latitude_longitude_active;");
            migrationBuilder.Sql(
                "CREATE INDEX ix_listings_approx_latitude_approx_longitude_active ON listing.listings (approx_latitude, approx_longitude) WHERE is_active;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_listings_approx_latitude",
                schema: "listing",
                table: "listings",
                sql: "approx_latitude BETWEEN -90 AND 90");

            migrationBuilder.AddCheckConstraint(
                name: "ck_listings_approx_longitude",
                schema: "listing",
                table: "listings",
                sql: "approx_longitude BETWEEN -180 AND 180");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS listing.ix_listings_approx_latitude_approx_longitude_active;");
            migrationBuilder.Sql(
                "CREATE INDEX ix_listings_latitude_longitude_active ON listing.listings (latitude, longitude) WHERE is_active;");

            migrationBuilder.DropCheckConstraint(
                name: "ck_listings_approx_latitude",
                schema: "listing",
                table: "listings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_listings_approx_longitude",
                schema: "listing",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "approx_latitude",
                schema: "listing",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "approx_longitude",
                schema: "listing",
                table: "listings");
        }
    }
}
