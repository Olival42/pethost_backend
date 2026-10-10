using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Hosts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateHostsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "host");

            migrationBuilder.CreateTable(
                name: "hosts",
                schema: "host",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    cpf = table.Column<string>(type: "char(11)", nullable: false),
                    cnpj = table.Column<string>(type: "char(14)", nullable: true),
                    legal_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    trade_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    stripe_account_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    deactivated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    suspended_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    company_city = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    company_complement = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    company_neighborhood = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    company_street_number = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    company_state = table.Column<string>(type: "char(2)", nullable: true),
                    company_street = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    company_zip_code = table.Column<string>(type: "char(8)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hosts", x => x.id);
                    table.CheckConstraint("ck_hosts_company_data", "(person_type = 'company' AND cnpj IS NOT NULL AND legal_name IS NOT NULL AND trade_name IS NOT NULL AND company_zip_code IS NOT NULL AND company_street IS NOT NULL AND company_street_number IS NOT NULL AND company_neighborhood IS NOT NULL AND company_city IS NOT NULL AND company_state IS NOT NULL) OR (person_type = 'individual' AND cnpj IS NULL AND legal_name IS NULL AND trade_name IS NULL AND company_zip_code IS NULL AND company_street IS NULL AND company_street_number IS NULL AND company_complement IS NULL AND company_neighborhood IS NULL AND company_city IS NULL AND company_state IS NULL)");
                    table.CheckConstraint("ck_hosts_person_type", "person_type IN ('individual', 'company')");
                });

            migrationBuilder.CreateIndex(
                name: "uq_hosts_cnpj",
                schema: "host",
                table: "hosts",
                column: "cnpj",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_hosts_cpf_individual",
                schema: "host",
                table: "hosts",
                column: "cpf",
                unique: true,
                filter: "person_type = 'individual'");

            migrationBuilder.CreateIndex(
                name: "uq_hosts_stripe_account_id",
                schema: "host",
                table: "hosts",
                column: "stripe_account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_hosts_user_id",
                schema: "host",
                table: "hosts",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hosts",
                schema: "host");
        }
    }
}
