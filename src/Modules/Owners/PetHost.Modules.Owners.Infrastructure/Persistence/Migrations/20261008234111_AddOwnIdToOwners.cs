using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Owners.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnIdToOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_owners",
                schema: "owner",
                table: "owners");

            migrationBuilder.AddColumn<Guid>(
                name: "id",
                schema: "owner",
                table: "owners",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddPrimaryKey(
                name: "pk_owners",
                schema: "owner",
                table: "owners",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "uq_owners_user_id",
                schema: "owner",
                table: "owners",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_owners",
                schema: "owner",
                table: "owners");

            migrationBuilder.DropIndex(
                name: "uq_owners_user_id",
                schema: "owner",
                table: "owners");

            migrationBuilder.DropColumn(
                name: "id",
                schema: "owner",
                table: "owners");

            migrationBuilder.AddPrimaryKey(
                name: "pk_owners",
                schema: "owner",
                table: "owners",
                column: "user_id");
        }
    }
}
