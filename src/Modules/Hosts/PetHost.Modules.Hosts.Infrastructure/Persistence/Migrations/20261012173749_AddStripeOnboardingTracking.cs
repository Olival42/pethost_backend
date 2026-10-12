using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Hosts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeOnboardingTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "stripe_account_created_at",
                schema: "host",
                table: "hosts",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "stripe_reminded_at",
                schema: "host",
                table: "hosts",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "stripe_reminders_sent",
                schema: "host",
                table: "hosts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Contas conectadas criadas antes desta coluna: a melhor aproximação de quando o
            // cadastro foi aberto é a última alteração do anfitrião.
            migrationBuilder.Sql(
                "UPDATE host.hosts SET stripe_account_created_at = updated_at WHERE stripe_account_id IS NOT NULL AND stripe_account_created_at IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "stripe_account_created_at",
                schema: "host",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "stripe_reminded_at",
                schema: "host",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "stripe_reminders_sent",
                schema: "host",
                table: "hosts");
        }
    }
}
