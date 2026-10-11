using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Modules.Hosts.Infrastructure.Persistence.Converters;

namespace PetHost.Modules.Hosts.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="HostApplication"/> em <c>host.host_applications</c> e as fotos em
/// <c>host.host_application_documents</c> (FK física: mesmo schema, <c>ON DELETE CASCADE</c>).
/// </summary>
/// <remarks>
/// O banco também garante as regras que importam: uma solicitação aberta por conta, um CPF
/// por solicitação aberta, uma foto por vaga, motivo de recusa só em solicitação recusada.
/// Concorrência otimista pelo <c>xmin</c> do Postgres: dois admins decidindo juntos não
/// passam os dois (§11).
/// </remarks>
internal sealed class HostApplicationConfiguration : IEntityTypeConfiguration<HostApplication>
{
    private static readonly string OpenStatuses =
        $"'{HostApplicationStatusValues.Draft}', '{HostApplicationStatusValues.Submitted}'";

    private const string CompanyColumnsFilled =
        "cnpj IS NOT NULL AND legal_name IS NOT NULL AND trade_name IS NOT NULL"
        + " AND company_zip_code IS NOT NULL AND company_street IS NOT NULL AND company_street_number IS NOT NULL"
        + " AND company_neighborhood IS NOT NULL AND company_city IS NOT NULL AND company_state IS NOT NULL";

    private const string CompanyColumnsEmpty =
        "cnpj IS NULL AND legal_name IS NULL AND trade_name IS NULL"
        + " AND company_zip_code IS NULL AND company_street IS NULL AND company_street_number IS NULL"
        + " AND company_complement IS NULL AND company_neighborhood IS NULL AND company_city IS NULL AND company_state IS NULL";

    private static readonly string RejectedStatuses =
        $"'{HostApplicationStatusValues.Rejected}', '{HostApplicationStatusValues.RejectedFinal}'";

    public void Configure(EntityTypeBuilder<HostApplication> builder)
    {
        builder.ToTable("host_applications", table =>
        {
            table.HasCheckConstraint("ck_host_applications_status", $"status IN ({Quoted(HostApplicationStatusValues.All)})");
            table.HasCheckConstraint("ck_host_applications_document_type", $"document_type IN ({Quoted(IdentityDocumentTypeValues.All)})");
            table.HasCheckConstraint(
                "ck_host_applications_person_type",
                $"person_type IN ('{PersonTypeValues.Individual}', '{PersonTypeValues.Company}')");
            table.HasCheckConstraint(
                "ck_host_applications_rejection_reason",
                $"(status IN ({RejectedStatuses}) AND rejection_reason IN ({Quoted(RejectionReasonValues.All)}))"
                + $" OR (status NOT IN ({RejectedStatuses}) AND rejection_reason IS NULL)");
            table.HasCheckConstraint("ck_host_applications_attempt_number", "attempt_number >= 1");

            // Mesma regra de hosts: empresa tem todos os dados da empresa; pessoa física, nenhum.
            table.HasCheckConstraint(
                "ck_host_applications_company_data",
                $"(person_type = '{PersonTypeValues.Company}' AND {CompanyColumnsFilled})"
                + $" OR (person_type = '{PersonTypeValues.Individual}' AND {CompanyColumnsEmpty})");
        });

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new HostApplicationId(value))
            .ValueGeneratedNever();

        builder.Property(a => a.UserId).HasColumnName("user_id").IsRequired();

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasConversion(status => HostApplicationStatusValues.ToWire(status), value => HostApplicationStatusValues.FromWire(value))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(a => a.PersonType)
            .HasColumnName("person_type")
            .HasConversion<PersonTypeConverter>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(a => a.Cpf)
            .HasColumnName("cpf")
            .HasConversion<CpfConverter>()
            .HasColumnType($"char({Cpf.Length})")
            .IsRequired();

        builder.Property(a => a.DocumentType)
            .HasColumnName("document_type")
            .HasConversion(type => IdentityDocumentTypeValues.ToWire(type), value => IdentityDocumentTypeValues.FromWire(value))
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(a => a.Cnpj)
            .HasColumnName("cnpj")
            .HasConversion<CnpjConverter>()
            .HasColumnType($"char({Cnpj.Length})");

        builder.Property(a => a.LegalName)
            .HasColumnName("legal_name")
            .HasMaxLength(Host.LegalNameMaxLength);

        builder.Property(a => a.TradeName)
            .HasColumnName("trade_name")
            .HasMaxLength(Host.TradeNameMaxLength);

        // Endereço da empresa: as mesmas colunas company_* de hosts, nulas na pessoa física.
        builder.ComplexProperty(a => a.CompanyAddress, address =>
        {
            address.Property(a => a.ZipCode).HasColumnName("company_zip_code").HasColumnType($"char({CompanyAddress.ZipCodeLength})");
            address.Property(a => a.Street).HasColumnName("company_street").HasMaxLength(CompanyAddress.StreetMaxLength);
            address.Property(a => a.Number).HasColumnName("company_street_number").HasMaxLength(CompanyAddress.NumberMaxLength);
            address.Property(a => a.Complement).HasColumnName("company_complement").HasMaxLength(CompanyAddress.ComplementMaxLength);
            address.Property(a => a.Neighborhood).HasColumnName("company_neighborhood").HasMaxLength(CompanyAddress.NeighborhoodMaxLength);
            address.Property(a => a.City).HasColumnName("company_city").HasMaxLength(CompanyAddress.CityMaxLength);
            address.Property(a => a.State).HasColumnName("company_state").HasColumnType($"char({CompanyAddress.StateLength})");
        });

        builder.Property(a => a.NoticeVersion)
            .HasColumnName("notice_version")
            .HasMaxLength(HostApplication.NoticeVersionMaxLength)
            .IsRequired();

        builder.Property(a => a.NoticeAcceptedAt).HasColumnName("notice_accepted_at").HasColumnType("timestamptz").IsRequired();

        builder.Property(a => a.NoticeIpAddress)
            .HasColumnName("notice_ip_address")
            .HasMaxLength(HostApplication.IpAddressMaxLength);

        builder.Property(a => a.AttemptNumber).HasColumnName("attempt_number").IsRequired();

        builder.Property(a => a.SubmittedAt).HasColumnName("submitted_at").HasColumnType("timestamptz");
        builder.Property(a => a.ReviewedAt).HasColumnName("reviewed_at").HasColumnType("timestamptz");
        builder.Property(a => a.ReviewedBy).HasColumnName("reviewed_by");

        builder.Property(a => a.RejectionReason)
            .HasColumnName("rejection_reason")
            .HasConversion(
                reason => reason == null ? null : RejectionReasonValues.ToWire(reason.Value),
                value => value == null ? null : RejectionReasonValues.FromWire(value))
            .HasMaxLength(30);

        builder.Property(a => a.ReviewNotes)
            .HasColumnName("review_notes")
            .HasMaxLength(HostApplication.ReviewNotesMaxLength);

        builder.Property(a => a.ClosedAt).HasColumnName("closed_at").HasColumnType("timestamptz");
        builder.Property(a => a.DocumentsPurgedAt).HasColumnName("documents_purged_at").HasColumnType("timestamptz");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        // xmin: coluna de sistema do Postgres, muda a cada UPDATE. Não entra na migration.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasMany(a => a.Documents)
            .WithOne()
            .HasForeignKey("ApplicationId")
            .HasConstraintName("fk_host_application_documents_host_applications_application_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(a => a.Documents).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(a => new { a.UserId, a.CreatedAt })
            .HasDatabaseName("ix_host_applications_user_id_created_at");

        // Uma solicitação aberta por conta (corrida de dois "começar" ao mesmo tempo).
        builder.HasIndex(a => a.UserId)
            .HasDatabaseName("uq_host_applications_user_id_open")
            .HasFilter($"status IN ({OpenStatuses})")
            .IsUnique();

        // O mesmo CPF não fica em duas solicitações abertas (de contas diferentes).
        builder.HasIndex(a => a.Cpf)
            .HasDatabaseName("uq_host_applications_cpf_open")
            .HasFilter($"status IN ({OpenStatuses})")
            .IsUnique();

        // O mesmo CNPJ não fica em duas solicitações abertas.
        builder.HasIndex(a => a.Cnpj)
            .HasDatabaseName("uq_host_applications_cnpj_open")
            .HasFilter($"cnpj IS NOT NULL AND status IN ({OpenStatuses})")
            .IsUnique();

        // A fila do admin.
        builder.HasIndex(a => new { a.Status, a.SubmittedAt })
            .HasDatabaseName("ix_host_applications_status_submitted_at");

        // A tarefa de limpeza: fechadas que ainda têm fotos.
        builder.HasIndex(a => a.ClosedAt)
            .HasDatabaseName("ix_host_applications_closed_at_pending_purge")
            .HasFilter("closed_at IS NOT NULL AND documents_purged_at IS NULL");

        builder.Ignore(a => a.DomainEvents);
        builder.Ignore(a => a.IsOpen);
        builder.Ignore(a => a.MissingDocuments);
        builder.Ignore(a => a.Company);
    }

    private static string Quoted(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"'{v}'"));
}
