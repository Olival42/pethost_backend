using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Bookings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateBookingsTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "booking");

            migrationBuilder.CreateTable(
                name: "bookings",
                schema: "booking",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    pet_count = table.Column<int>(type: "integer", nullable: false),
                    nightly_rate = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    total_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    responded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    checked_in_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    checked_out_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    expired_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    cancelled_by = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    decline_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    refund_percentage = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    check_in_date = table.Column<DateOnly>(type: "date", nullable: false),
                    check_out_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookings", x => x.id);
                    table.CheckConstraint("ck_bookings_amounts", "nightly_rate >= 0 AND total_price >= 0");
                    table.CheckConstraint("ck_bookings_cancelled_by", "cancelled_by IS NULL OR cancelled_by IN ('owner', 'host')");
                    table.CheckConstraint("ck_bookings_period", "check_out_date > check_in_date");
                    table.CheckConstraint("ck_bookings_pet_count", "pet_count BETWEEN 1 AND 10");
                    table.CheckConstraint("ck_bookings_refund_percentage", "refund_percentage IS NULL OR refund_percentage BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_bookings_status", "status IN ('requested', 'accepted', 'confirmed', 'in_progress', 'completed', 'declined', 'expired', 'cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "booking_pets",
                schema: "booking",
                columns: table => new
                {
                    pet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_booking_pets", x => new { x.booking_id, x.pet_id });
                    table.ForeignKey(
                        name: "fk_booking_pets_bookings_booking_id",
                        column: x => x.booking_id,
                        principalSchema: "booking",
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_booking_pets_pet_id",
                schema: "booking",
                table: "booking_pets",
                column: "pet_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_status_requested_at",
                schema: "booking",
                table: "bookings",
                columns: new[] { "status", "requested_at" });

            // Índices com as datas (colunas do tipo complexo StayPeriod, que o EF não indexa):
            // capacidade e pet já reservado no período; "minhas reservas" do tutor e do anfitrião.
            migrationBuilder.Sql(
                "CREATE INDEX ix_bookings_listing_id_check_in_date_check_out_date ON booking.bookings (listing_id, check_in_date, check_out_date);");
            migrationBuilder.Sql(
                "CREATE INDEX ix_bookings_owner_id_check_in_date ON booking.bookings (owner_id, check_in_date DESC);");
            migrationBuilder.Sql(
                "CREATE INDEX ix_bookings_host_id_check_in_date ON booking.bookings (host_id, check_in_date DESC);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS booking.ix_bookings_host_id_check_in_date;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS booking.ix_bookings_owner_id_check_in_date;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS booking.ix_bookings_listing_id_check_in_date_check_out_date;");

            migrationBuilder.DropTable(
                name: "booking_pets",
                schema: "booking");

            migrationBuilder.DropTable(
                name: "bookings",
                schema: "booking");
        }
    }
}
