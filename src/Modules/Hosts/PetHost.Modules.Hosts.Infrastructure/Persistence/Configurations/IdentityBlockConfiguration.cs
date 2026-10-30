using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetHost.Modules.Hosts.Domain.IdentityBlocks;

namespace PetHost.Modules.Hosts.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="IdentityBlock"/> em <c>host.identity_blocks</c>. Só a impressão do CPF (HMAC),
/// nunca o CPF; uma linha por CPF (índice único). Fica depois da exclusão da conta.
/// </summary>
internal sealed class IdentityBlockConfiguration : IEntityTypeConfiguration<IdentityBlock>
{
    public void Configure(EntityTypeBuilder<IdentityBlock> builder)
    {
        builder.ToTable("identity_blocks", table =>
            table.HasCheckConstraint(
                "ck_identity_blocks_reason",
                $"reason IN ('{IdentityBlockReasonValues.Fraud}', '{IdentityBlockReasonValues.AttemptsExhausted}')"));

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new IdentityBlockId(value))
            .ValueGeneratedNever();

        builder.Property(b => b.CpfFingerprint)
            .HasColumnName("cpf_fingerprint")
            .HasColumnType($"char({IdentityBlock.FingerprintLength})")
            .IsRequired();

        builder.Property(b => b.Reason)
            .HasColumnName("reason")
            .HasConversion(reason => IdentityBlockReasonValues.ToWire(reason), value => IdentityBlockReasonValues.FromWire(value))
            .HasMaxLength(20)
            .IsRequired();

        // Sem FK: a solicitação pode ser apagada (exclusão da conta) e o bloqueio fica.
        builder.Property(b => b.ApplicationId)
            .HasColumnName("application_id")
            .IsRequired();

        builder.Property(b => b.BlockedAt)
            .HasColumnName("blocked_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(b => b.CpfFingerprint)
            .HasDatabaseName("uq_identity_blocks_cpf_fingerprint")
            .IsUnique();

        builder.Ignore(b => b.DomainEvents);
    }
}
