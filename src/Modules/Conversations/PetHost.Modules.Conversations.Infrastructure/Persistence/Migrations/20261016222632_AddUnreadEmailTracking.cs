using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Conversations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnreadEmailTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "host_unread_notified_at",
                schema: "conversation",
                table: "conversations",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "owner_unread_notified_at",
                schema: "conversation",
                table: "conversations",
                type: "timestamptz",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "host_unread_notified_at",
                schema: "conversation",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "owner_unread_notified_at",
                schema: "conversation",
                table: "conversations");
        }
    }
}
