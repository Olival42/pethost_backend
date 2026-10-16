using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLateFeesToPaymentInstallments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_installments_refunded_cents",
                schema: "payment",
                table: "payment_installments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_installments_split",
                schema: "payment",
                table: "payment_installments");

            migrationBuilder.AddColumn<long>(
                name: "late_fee_cents",
                schema: "payment",
                table: "payment_installments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "overdue_reminded_at",
                schema: "payment",
                table: "payment_installments",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_installments_late_fee_cents",
                schema: "payment",
                table: "payment_installments",
                sql: "late_fee_cents >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_installments_refunded_cents",
                schema: "payment",
                table: "payment_installments",
                sql: "refunded_cents BETWEEN 0 AND amount_cents + late_fee_cents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_installments_split",
                schema: "payment",
                table: "payment_installments",
                sql: "host_payout_cents >= 0 AND platform_fee_cents >= 0 AND host_payout_cents + platform_fee_cents <= amount_cents + late_fee_cents");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_installments_late_fee_cents",
                schema: "payment",
                table: "payment_installments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_installments_refunded_cents",
                schema: "payment",
                table: "payment_installments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_installments_split",
                schema: "payment",
                table: "payment_installments");

            migrationBuilder.DropColumn(
                name: "late_fee_cents",
                schema: "payment",
                table: "payment_installments");

            migrationBuilder.DropColumn(
                name: "overdue_reminded_at",
                schema: "payment",
                table: "payment_installments");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_installments_refunded_cents",
                schema: "payment",
                table: "payment_installments",
                sql: "refunded_cents BETWEEN 0 AND amount_cents");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_installments_split",
                schema: "payment",
                table: "payment_installments",
                sql: "host_payout_cents >= 0 AND platform_fee_cents >= 0 AND host_payout_cents + platform_fee_cents <= amount_cents");
        }
    }
}
