using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PetHost.Modules.Hosts.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHostApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_hosts_cnpj",
                schema: "host",
                table: "hosts");

            migrationBuilder.DropIndex(
                name: "uq_hosts_cpf_individual",
                schema: "host",
                table: "hosts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_hosts_company_data",
                schema: "host",
                table: "hosts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_hosts_person_type",
                schema: "host",
                table: "hosts");

            // person_type passa de individual/company para pf/pj (as novas checks vêm abaixo).
            migrationBuilder.Sql(
                "UPDATE host.hosts SET person_type = CASE person_type WHEN 'individual' THEN 'pf' WHEN 'company' THEN 'pj' ELSE person_type END;");

            migrationBuilder.AddColumn<Guid>(
                name: "application_id",
                schema: "host",
                table: "hosts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "identity_released_at",
                schema: "host",
                table: "hosts",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "stripe_charges_enabled",
                schema: "host",
                table: "hosts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "stripe_details_submitted",
                schema: "host",
                table: "hosts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "stripe_disabled_reason",
                schema: "host",
                table: "hosts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "stripe_onboarded_at",
                schema: "host",
                table: "hosts",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "stripe_payouts_enabled",
                schema: "host",
                table: "hosts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "stripe_synced_at",
                schema: "host",
                table: "hosts",
                type: "timestamptz",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "verified_at",
                schema: "host",
                table: "hosts",
                type: "timestamptz",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "verified_by",
                schema: "host",
                table: "hosts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "host_applications",
                schema: "host",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    person_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    cpf = table.Column<string>(type: "char(11)", nullable: false),
                    cnpj = table.Column<string>(type: "char(14)", nullable: true),
                    legal_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    trade_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    document_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    notice_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notice_accepted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    notice_ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    review_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    documents_purged_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
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
                    table.PrimaryKey("pk_host_applications", x => x.id);
                    table.CheckConstraint("ck_host_applications_attempt_number", "attempt_number >= 1");
                    table.CheckConstraint("ck_host_applications_company_data", "(person_type = 'pj' AND cnpj IS NOT NULL AND legal_name IS NOT NULL AND trade_name IS NOT NULL AND company_zip_code IS NOT NULL AND company_street IS NOT NULL AND company_street_number IS NOT NULL AND company_neighborhood IS NOT NULL AND company_city IS NOT NULL AND company_state IS NOT NULL) OR (person_type = 'pf' AND cnpj IS NULL AND legal_name IS NULL AND trade_name IS NULL AND company_zip_code IS NULL AND company_street IS NULL AND company_street_number IS NULL AND company_complement IS NULL AND company_neighborhood IS NULL AND company_city IS NULL AND company_state IS NULL)");
                    table.CheckConstraint("ck_host_applications_document_type", "document_type IN ('rg', 'cnh', 'cin')");
                    table.CheckConstraint("ck_host_applications_person_type", "person_type IN ('pf', 'pj')");
                    table.CheckConstraint("ck_host_applications_rejection_reason", "(status IN ('rejected', 'rejected_final') AND rejection_reason IN ('document_illegible', 'document_expired', 'selfie_mismatch', 'data_mismatch', 'missing_document', 'suspected_fraud', 'other')) OR (status NOT IN ('rejected', 'rejected_final') AND rejection_reason IS NULL)");
                    table.CheckConstraint("ck_host_applications_status", "status IN ('draft', 'submitted', 'approved', 'rejected', 'rejected_final', 'cancelled', 'expired')");
                });

            migrationBuilder.CreateTable(
                name: "host_application_documents",
                schema: "host",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_hash = table.Column<string>(type: "char(64)", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_host_application_documents", x => x.id);
                    table.CheckConstraint("ck_host_application_documents_kind", "kind IN ('front', 'back', 'selfie')");
                    table.ForeignKey(
                        name: "fk_host_application_documents_host_applications_application_id",
                        column: x => x.application_id,
                        principalSchema: "host",
                        principalTable: "host_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_hosts_application_id",
                schema: "host",
                table: "hosts",
                column: "application_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_hosts_cnpj",
                schema: "host",
                table: "hosts",
                column: "cnpj",
                unique: true,
                filter: "cnpj IS NOT NULL AND identity_released_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_hosts_cpf_individual",
                schema: "host",
                table: "hosts",
                column: "cpf",
                unique: true,
                filter: "person_type = 'pf' AND identity_released_at IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_hosts_company_data",
                schema: "host",
                table: "hosts",
                sql: "(person_type = 'pj' AND cnpj IS NOT NULL AND legal_name IS NOT NULL AND trade_name IS NOT NULL AND company_zip_code IS NOT NULL AND company_street IS NOT NULL AND company_street_number IS NOT NULL AND company_neighborhood IS NOT NULL AND company_city IS NOT NULL AND company_state IS NOT NULL) OR (person_type = 'pf' AND cnpj IS NULL AND legal_name IS NULL AND trade_name IS NULL AND company_zip_code IS NULL AND company_street IS NULL AND company_street_number IS NULL AND company_complement IS NULL AND company_neighborhood IS NULL AND company_city IS NULL AND company_state IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_hosts_person_type",
                schema: "host",
                table: "hosts",
                sql: "person_type IN ('pf', 'pj')");

            migrationBuilder.CreateIndex(
                name: "uq_host_application_documents_application_id_kind",
                schema: "host",
                table: "host_application_documents",
                columns: new[] { "application_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_host_applications_closed_at_pending_purge",
                schema: "host",
                table: "host_applications",
                column: "closed_at",
                filter: "closed_at IS NOT NULL AND documents_purged_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_host_applications_status_submitted_at",
                schema: "host",
                table: "host_applications",
                columns: new[] { "status", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_host_applications_user_id_created_at",
                schema: "host",
                table: "host_applications",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "uq_host_applications_cnpj_open",
                schema: "host",
                table: "host_applications",
                column: "cnpj",
                unique: true,
                filter: "cnpj IS NOT NULL AND status IN ('draft', 'submitted')");

            migrationBuilder.CreateIndex(
                name: "uq_host_applications_cpf_open",
                schema: "host",
                table: "host_applications",
                column: "cpf",
                unique: true,
                filter: "status IN ('draft', 'submitted')");

            migrationBuilder.CreateIndex(
                name: "uq_host_applications_user_id_open",
                schema: "host",
                table: "host_applications",
                column: "user_id",
                unique: true,
                filter: "status IN ('draft', 'submitted')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "host_application_documents",
                schema: "host");

            migrationBuilder.DropTable(
                name: "host_applications",
                schema: "host");

            migrationBuilder.DropIndex(
                name: "uq_hosts_application_id",
                schema: "host",
                table: "hosts");

            migrationBuilder.DropIndex(
                name: "uq_hosts_cnpj",
                schema: "host",
                table: "hosts");

            migrationBuilder.DropIndex(
                name: "uq_hosts_cpf_individual",
                schema: "host",
                table: "hosts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_hosts_company_data",
                schema: "host",
                table: "hosts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_hosts_person_type",
                schema: "host",
                table: "hosts");

            migrationBuilder.Sql(
                "UPDATE host.hosts SET person_type = CASE person_type WHEN 'pf' THEN 'individual' WHEN 'pj' THEN 'company' ELSE person_type END;");

            migrationBuilder.DropColumn(
                name: "application_id",
                schema: "host",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "identity_released_at",
                schema: "host",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "stripe_charges_enabled",
                schema: "host",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "stripe_details_submitted",
                schema: "host",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "stripe_disabled_reason",
                schema: "host",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "stripe_onboarded_at",
                schema: "host",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "stripe_payouts_enabled",
                schema: "host",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "stripe_synced_at",
                schema: "host",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "verified_at",
                schema: "host",
                table: "hosts");

            migrationBuilder.DropColumn(
                name: "verified_by",
                schema: "host",
                table: "hosts");

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

            migrationBuilder.AddCheckConstraint(
                name: "ck_hosts_company_data",
                schema: "host",
                table: "hosts",
                sql: "(person_type = 'company' AND cnpj IS NOT NULL AND legal_name IS NOT NULL AND trade_name IS NOT NULL AND company_zip_code IS NOT NULL AND company_street IS NOT NULL AND company_street_number IS NOT NULL AND company_neighborhood IS NOT NULL AND company_city IS NOT NULL AND company_state IS NOT NULL) OR (person_type = 'individual' AND cnpj IS NULL AND legal_name IS NULL AND trade_name IS NULL AND company_zip_code IS NULL AND company_street IS NULL AND company_street_number IS NULL AND company_complement IS NULL AND company_neighborhood IS NULL AND company_city IS NULL AND company_state IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_hosts_person_type",
                schema: "host",
                table: "hosts",
                sql: "person_type IN ('individual', 'company')");
        }
    }
}
