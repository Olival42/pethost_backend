using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Hosts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStripeAccountReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "stripe_account_resets",
                schema: "host",
                table: "hosts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "stripe_rejected_account_id",
                schema: "host",
                table: "hosts",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "stripe_account_resets",
                schema: "host",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "stripe_rejected_account_id",
                schema: "host",
                table: "hosts");
        }
    }
}
