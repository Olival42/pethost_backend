using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreatePaymentsTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payment");

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    host_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    installment_count = table.Column<int>(type: "integer", nullable: false),
                    plan_revision = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    refund_percentage = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    settled_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    currency = table.Column<string>(type: "char(3)", nullable: false, defaultValue: "brl"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount_cents", "amount_cents > 0");
                    table.CheckConstraint("ck_payments_currency", "currency = 'brl'");
                    table.CheckConstraint("ck_payments_installment_count", "installment_count BETWEEN 1 AND 6");
                    table.CheckConstraint("ck_payments_refund_percentage", "refund_percentage IS NULL OR refund_percentage BETWEEN 0 AND 100");
                    table.CheckConstraint("ck_payments_status", "status IN ('pending', 'active', 'paid', 'cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "stripe_events",
                schema: "payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stripe_event_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stripe_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_installments",
                schema: "payment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    amount_cents = table.Column<long>(type: "bigint", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    stripe_payment_intent_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    intent_open = table.Column<bool>(type: "boolean", nullable: false),
                    payment_attempts = table.Column<int>(type: "integer", nullable: false),
                    stripe_charge_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    reminder_sent_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    overdue_since = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    refund_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    refunded_cents = table.Column<long>(type: "bigint", nullable: false),
                    stripe_refund_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    refunded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    payout_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    host_payout_cents = table.Column<long>(type: "bigint", nullable: false),
                    platform_fee_cents = table.Column<long>(type: "bigint", nullable: false),
                    stripe_transfer_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    transferred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    payout_failure_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reversed_cents = table.Column<long>(type: "bigint", nullable: false),
                    stripe_transfer_reversal_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    dispute_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    stripe_dispute_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    disputed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    dispute_closed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_installments", x => x.id);
                    table.CheckConstraint("ck_payment_installments_amount_cents", "amount_cents > 0");
                    table.CheckConstraint("ck_payment_installments_dispute_status", "dispute_status IN ('none', 'open', 'won', 'lost')");
                    table.CheckConstraint("ck_payment_installments_method", "method IS NULL OR method IN ('card', 'pix')");
                    table.CheckConstraint("ck_payment_installments_number", "number BETWEEN 1 AND 6");
                    table.CheckConstraint("ck_payment_installments_payout_status", "payout_status IN ('held', 'transferred', 'cancelled', 'reversed')");
                    table.CheckConstraint("ck_payment_installments_refund_status", "refund_status IN ('none', 'pending', 'succeeded', 'failed')");
                    table.CheckConstraint("ck_payment_installments_refunded_cents", "refunded_cents BETWEEN 0 AND amount_cents");
                    table.CheckConstraint("ck_payment_installments_reversed_cents", "reversed_cents BETWEEN 0 AND host_payout_cents");
                    table.CheckConstraint("ck_payment_installments_split", "host_payout_cents >= 0 AND platform_fee_cents >= 0 AND host_payout_cents + platform_fee_cents <= amount_cents");
                    table.CheckConstraint("ck_payment_installments_status", "status IN ('pending', 'processing', 'paid', 'overdue', 'cancelled')");
                    table.ForeignKey(
                        name: "fk_payment_installments_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "payment",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_installments_status",
                schema: "payment",
                table: "payment_installments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "uq_payment_installments_payment_id_number",
                schema: "payment",
                table: "payment_installments",
                columns: new[] { "payment_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_payment_installments_stripe_payment_intent_id",
                schema: "payment",
                table: "payment_installments",
                column: "stripe_payment_intent_id",
                unique: true,
                filter: "stripe_payment_intent_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payments_host_id_created_at",
                schema: "payment",
                table: "payments",
                columns: new[] { "host_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_owner_id",
                schema: "payment",
                table: "payments",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_updated_at_unsettled",
                schema: "payment",
                table: "payments",
                column: "updated_at",
                filter: "settled_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_payments_booking_id",
                schema: "payment",
                table: "payments",
                column: "booking_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_stripe_events_stripe_event_id",
                schema: "payment",
                table: "stripe_events",
                column: "stripe_event_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_installments",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "stripe_events",
                schema: "payment");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "payment");
        }
    }
}
