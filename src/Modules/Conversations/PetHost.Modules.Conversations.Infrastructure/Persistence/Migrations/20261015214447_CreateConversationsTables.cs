using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Conversations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateConversationsTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "conversation");

            migrationBuilder.CreateTable(
                name: "conversations",
                schema: "conversation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                schema: "conversation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sent_by = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    body = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    image_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.CheckConstraint("ck_messages_content", "body IS NOT NULL OR image_url IS NOT NULL");
                    table.CheckConstraint("ck_messages_sent_by", "sent_by IN ('owner', 'host')");
                    table.ForeignKey(
                        name: "fk_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "conversation",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_host_id_last_message_at",
                schema: "conversation",
                table: "conversations",
                columns: new[] { "host_id", "last_message_at" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_owner_id_last_message_at",
                schema: "conversation",
                table: "conversations",
                columns: new[] { "owner_id", "last_message_at" });

            migrationBuilder.CreateIndex(
                name: "uq_conversations_owner_id_host_id",
                schema: "conversation",
                table: "conversations",
                columns: new[] { "owner_id", "host_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_messages_booking_id",
                schema: "conversation",
                table: "messages",
                column: "booking_id",
                filter: "booking_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_id_created_at",
                schema: "conversation",
                table: "messages",
                columns: new[] { "conversation_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_id_sent_by_unread",
                schema: "conversation",
                table: "messages",
                columns: new[] { "conversation_id", "sent_by" },
                filter: "read_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "messages",
                schema: "conversation");

            migrationBuilder.DropTable(
                name: "conversations",
                schema: "conversation");
        }
    }
}
