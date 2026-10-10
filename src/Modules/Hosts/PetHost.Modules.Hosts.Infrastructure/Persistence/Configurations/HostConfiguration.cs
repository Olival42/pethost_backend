using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Modules.Hosts.Infrastructure.Persistence.Converters;

namespace PetHost.Modules.Hosts.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="Host"/> na tabela <c>host.hosts</c>. Chave própria (<c>id</c>);
/// <c>user_id</c> aponta para a conta no Auth como <b>FK lógica</b>, com índice único
/// (uma conta, um anfitrião). Sem constraint física: cada módulo tem o seu schema (§5, §11).
/// </summary>
/// <remarks>
/// Os CHECKs garantem no banco o que o domínio já garante: pessoa jurídica tem CNPJ, razão
/// social, nome fantasia e endereço da empresa; pessoa física não tem nenhum deles.
/// </remarks>
internal sealed class HostConfiguration : IEntityTypeConfiguration<Host>
{
    private const string CompanyColumnsFilled =
        "cnpj IS NOT NULL AND legal_name IS NOT NULL AND trade_name IS NOT NULL"
        + " AND company_zip_code IS NOT NULL AND company_street IS NOT NULL AND company_street_number IS NOT NULL"
        + " AND company_neighborhood IS NOT NULL AND company_city IS NOT NULL AND company_state IS NOT NULL";

    private const string CompanyColumnsEmpty =
        "cnpj IS NULL AND legal_name IS NULL AND trade_name IS NULL"
        + " AND company_zip_code IS NULL AND company_street IS NULL AND company_street_number IS NULL"
        + " AND company_complement IS NULL AND company_neighborhood IS NULL AND company_city IS NULL AND company_state IS NULL";

    public void Configure(EntityTypeBuilder<Host> builder)
    {
        builder.ToTable("hosts", table =>
        {
            table.HasCheckConstraint(
                "ck_hosts_person_type",
                $"person_type IN ('{PersonTypeValues.Individual}', '{PersonTypeValues.Company}')");

            table.HasCheckConstraint(
                "ck_hosts_company_data",
                $"(person_type = '{PersonTypeValues.Company}' AND {CompanyColumnsFilled})"
                + $" OR (person_type = '{PersonTypeValues.Individual}' AND {CompanyColumnsEmpty})");
        });

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Id)
            .HasColumnName("id")
            .HasConversion<HostIdConverter>()
            .ValueGeneratedNever();

        builder.Property(h => h.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(h => h.PersonType)
            .HasColumnName("person_type")
            .HasConversion<PersonTypeConverter>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(h => h.Cpf)
            .HasColumnName("cpf")
            .HasConversion<CpfConverter>()
            .HasColumnType($"char({Cpf.Length})")
            .IsRequired();

        builder.Property(h => h.Cnpj)
            .HasColumnName("cnpj")
            .HasConversion<CnpjConverter>()
            .HasColumnType($"char({Cnpj.Length})");

        builder.Property(h => h.LegalName)
            .HasColumnName("legal_name")
            .HasMaxLength(Host.LegalNameMaxLength);

        builder.Property(h => h.TradeName)
            .HasColumnName("trade_name")
            .HasMaxLength(Host.TradeNameMaxLength);

        // Endereço da empresa como tipo complexo opcional: colunas company_* na própria
        // hosts, nulas na pessoa física.
        builder.ComplexProperty(h => h.CompanyAddress, address =>
        {
            address.Property(a => a.ZipCode)
                .HasColumnName("company_zip_code")
                .HasColumnType($"char({CompanyAddress.ZipCodeLength})");

            address.Property(a => a.Street)
                .HasColumnName("company_street")
                .HasMaxLength(CompanyAddress.StreetMaxLength);

            address.Property(a => a.Number)
                .HasColumnName("company_street_number")
                .HasMaxLength(CompanyAddress.NumberMaxLength);

            address.Property(a => a.Complement)
                .HasColumnName("company_complement")
                .HasMaxLength(CompanyAddress.ComplementMaxLength);

            address.Property(a => a.Neighborhood)
                .HasColumnName("company_neighborhood")
                .HasMaxLength(CompanyAddress.NeighborhoodMaxLength);

            address.Property(a => a.City)
                .HasColumnName("company_city")
                .HasMaxLength(CompanyAddress.CityMaxLength);

            address.Property(a => a.State)
                .HasColumnName("company_state")
                .HasColumnType($"char({CompanyAddress.StateLength})");
        });

        builder.Property(h => h.StripeAccountId)
            .HasColumnName("stripe_account_id")
            .HasMaxLength(Host.StripeAccountIdMaxLength);

        builder.Property(h => h.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(h => h.DeactivatedAt)
            .HasColumnName("deactivated_at")
            .HasColumnType("timestamptz");

        builder.Property(h => h.SuspendedAt)
            .HasColumnName("suspended_at")
            .HasColumnType("timestamptz");

        builder.Property(h => h.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(h => h.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(h => h.UserId)
            .HasDatabaseName("uq_hosts_user_id")
            .IsUnique();

        // Um CPF por anfitrião pessoa física. Na empresa o CPF é do representante, que pode
        // representar mais de uma: lá quem é único é o CNPJ.
        builder.HasIndex(h => h.Cpf)
            .HasDatabaseName("uq_hosts_cpf_individual")
            .HasFilter($"person_type = '{PersonTypeValues.Individual}'")
            .IsUnique();

        builder.HasIndex(h => h.Cnpj)
            .HasDatabaseName("uq_hosts_cnpj")
            .IsUnique();

        builder.HasIndex(h => h.StripeAccountId)
            .HasDatabaseName("uq_hosts_stripe_account_id")
            .IsUnique();

        builder.Ignore(h => h.DomainEvents);
    }
}
