using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Owners.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropStripeCustomerIdFromOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_owners_stripe_customer_id",
                schema: "owner",
                table: "owners");

            migrationBuilder.DropColumn(
                name: "stripe_customer_id",
                schema: "owner",
                table: "owners");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "stripe_customer_id",
                schema: "owner",
                table: "owners",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "uq_owners_stripe_customer_id",
                schema: "owner",
                table: "owners",
                column: "stripe_customer_id",
                unique: true);
        }
    }
}
