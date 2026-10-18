using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Bookings.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCareTermsAndDeadlineRemindersToBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "care_terms_accepted_at",
                schema: "booking",
                table: "bookings",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "payment_reminded_at",
                schema: "booking",
                table: "bookings",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "response_reminded_at",
                schema: "booking",
                table: "bookings",
                type: "timestamptz",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "care_terms_accepted_at",
                schema: "booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "payment_reminded_at",
                schema: "booking",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "response_reminded_at",
                schema: "booking",
                table: "bookings");
        }
    }
}
