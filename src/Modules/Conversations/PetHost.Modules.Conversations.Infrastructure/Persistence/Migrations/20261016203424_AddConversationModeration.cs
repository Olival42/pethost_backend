using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Conversations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "blocked_at",
                schema: "conversation",
                table: "conversations",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "blocked_by",
                schema: "conversation",
                table: "conversations",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "message_reports",
                schema: "conversation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_by = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_reports", x => x.id);
                    table.CheckConstraint("ck_message_reports_reason", "reason IN ('spam', 'harassment', 'scam', 'inappropriate', 'other')");
                    table.CheckConstraint("ck_message_reports_reported_by", "reported_by IN ('owner', 'host')");
                    table.CheckConstraint("ck_message_reports_resolution", "(status = 'pending' AND resolved_at IS NULL AND resolved_by IS NULL AND resolution_note IS NULL) OR (status <> 'pending' AND resolved_at IS NOT NULL AND resolved_by IS NOT NULL AND resolution_note IS NOT NULL)");
                    table.CheckConstraint("ck_message_reports_status", "status IN ('pending', 'upheld', 'dismissed')");
                    table.ForeignKey(
                        name: "fk_message_reports_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "conversation",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_message_reports_messages_message_id",
                        column: x => x.message_id,
                        principalSchema: "conversation",
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_conversations_block",
                schema: "conversation",
                table: "conversations",
                sql: "(blocked_by IS NULL) = (blocked_at IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_conversations_blocked_by",
                schema: "conversation",
                table: "conversations",
                sql: "blocked_by IS NULL OR blocked_by IN ('owner', 'host')");

            migrationBuilder.CreateIndex(
                name: "ix_message_reports_conversation_id_pending",
                schema: "conversation",
                table: "message_reports",
                column: "conversation_id",
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_message_reports_status_created_at",
                schema: "conversation",
                table: "message_reports",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "uq_message_reports_message_id",
                schema: "conversation",
                table: "message_reports",
                column: "message_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_reports",
                schema: "conversation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_conversations_block",
                schema: "conversation",
                table: "conversations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_conversations_blocked_by",
                schema: "conversation",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "blocked_at",
                schema: "conversation",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "blocked_by",
                schema: "conversation",
                table: "conversations");
        }
    }
}
