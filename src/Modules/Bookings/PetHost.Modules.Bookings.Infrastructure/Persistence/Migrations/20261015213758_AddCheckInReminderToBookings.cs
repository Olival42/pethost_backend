using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Bookings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCheckInReminderToBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "check_in_reminded_at",
                schema: "booking",
                table: "bookings",
                type: "timestamptz",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "check_in_reminded_at",
                schema: "booking",
                table: "bookings");
        }
    }
}
