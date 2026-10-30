using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Conversations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImageKeyToMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_messages_content",
                schema: "conversation",
                table: "messages");

            migrationBuilder.AddColumn<string>(
                name: "image_key",
                schema: "conversation",
                table: "messages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_messages_content",
                schema: "conversation",
                table: "messages",
                sql: "body IS NOT NULL OR image_url IS NOT NULL OR image_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_messages_content",
                schema: "conversation",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "image_key",
                schema: "conversation",
                table: "messages");

            migrationBuilder.AddCheckConstraint(
                name: "ck_messages_content",
                schema: "conversation",
                table: "messages",
                sql: "body IS NOT NULL OR image_url IS NOT NULL");
        }
    }
}
