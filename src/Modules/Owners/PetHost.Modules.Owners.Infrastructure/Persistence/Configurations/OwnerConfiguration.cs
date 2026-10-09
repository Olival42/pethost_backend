using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Modules.Owners.Infrastructure.Persistence.Converters;

namespace PetHost.Modules.Owners.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="Owner"/> na tabela <c>owner.owners</c>. Chave própria (<c>id</c>);
/// <c>user_id</c> aponta para a conta no Auth como <b>FK lógica</b>, com índice único
/// (uma conta, um tutor). Sem constraint física: cada módulo tem o seu schema e não há
/// restrição nem JOIN entre schemas (§5, §11). A ligação é garantida pela aplicação.
/// </summary>
internal sealed class OwnerConfiguration : IEntityTypeConfiguration<Owner>
{
    public void Configure(EntityTypeBuilder<Owner> builder)
    {
        // CPF nulo só em tutor suspenso: o admin libera o CPF de uma conta indevida.
        builder.ToTable("owners", table => table.HasCheckConstraint(
            "ck_owners_cpf_only_null_when_suspended",
            "cpf IS NOT NULL OR suspended_at IS NOT NULL"));

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasColumnName("id")
            .HasConversion<OwnerIdConverter>()
            .ValueGeneratedNever();

        builder.Property(o => o.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(o => o.Cpf)
            .HasColumnName("cpf")
            .HasConversion<CpfConverter>()
            .HasColumnType($"char({Cpf.Length})");

        builder.Property(o => o.StripeCustomerId)
            .HasColumnName("stripe_customer_id")
            .HasMaxLength(Owner.StripeCustomerIdMaxLength);

        builder.Property(o => o.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(o => o.DeactivatedAt)
            .HasColumnName("deactivated_at")
            .HasColumnType("timestamptz");

        builder.Property(o => o.SuspendedAt)
            .HasColumnName("suspended_at")
            .HasColumnType("timestamptz");

        builder.Property(o => o.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(o => o.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(o => o.UserId)
            .HasDatabaseName("uq_owners_user_id")
            .IsUnique();

        builder.HasIndex(o => o.Cpf)
            .HasDatabaseName("uq_owners_cpf")
            .IsUnique();

        builder.HasIndex(o => o.StripeCustomerId)
            .HasDatabaseName("uq_owners_stripe_customer_id")
            .IsUnique();

        builder.Ignore(o => o.DomainEvents);
    }
}
