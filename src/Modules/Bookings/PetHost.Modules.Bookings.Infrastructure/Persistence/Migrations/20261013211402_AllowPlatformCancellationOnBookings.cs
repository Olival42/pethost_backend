using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Bookings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowPlatformCancellationOnBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_bookings_cancelled_by",
                schema: "booking",
                table: "bookings");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bookings_cancelled_by",
                schema: "booking",
                table: "bookings",
                sql: "cancelled_by IS NULL OR cancelled_by IN ('owner', 'host', 'platform')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_bookings_cancelled_by",
                schema: "booking",
                table: "bookings");

            migrationBuilder.AddCheckConstraint(
                name: "ck_bookings_cancelled_by",
                schema: "booking",
                table: "bookings",
                sql: "cancelled_by IS NULL OR cancelled_by IN ('owner', 'host')");
        }
    }
}
