using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Modules.Payments.Infrastructure.Persistence.Converters;

namespace PetHost.Modules.Payments.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="Payment"/> em <c>payment.payments</c> e as parcelas em
/// <c>payment.payment_installments</c>. <c>booking_id</c>, <c>owner_id</c> e <c>host_id</c> são
/// <b>FKs lógicas</b>, sem constraint física: cada módulo tem o seu schema (§5, §11).
/// </summary>
/// <remarks>
/// Dinheiro em centavos inteiros (<c>bigint</c>), como o Stripe trabalha. Os CHECKs repetem no banco
/// as regras do domínio. Concorrência otimista pelo <c>xmin</c> do pagamento: toda mudança numa
/// parcela também atualiza o pagamento, então o webhook e o tutor mexendo ao mesmo tempo não
/// gravam os dois.
/// </remarks>
internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", table =>
        {
            table.HasCheckConstraint("ck_payments_status", $"status IN ({InList(PaymentStatusValues.All)})");
            table.HasCheckConstraint("ck_payments_amount_cents", "amount_cents > 0");
            table.HasCheckConstraint("ck_payments_installment_count", $"installment_count BETWEEN 1 AND {Payment.MaxInstallments}");
            table.HasCheckConstraint("ck_payments_refund_percentage", "refund_percentage IS NULL OR refund_percentage BETWEEN 0 AND 100");
            table.HasCheckConstraint("ck_payments_currency", $"currency = '{Payment.Currency}'");
        });

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new PaymentId(value))
            .ValueGeneratedNever();

        builder.Property(p => p.BookingId).HasColumnName("booking_id").IsRequired();
        builder.Property(p => p.OwnerId).HasColumnName("owner_id").IsRequired();
        builder.Property(p => p.HostId).HasColumnName("host_id").IsRequired();
        builder.Property(p => p.AmountCents).HasColumnName("amount_cents").IsRequired();

        builder.Property<string>("Currency")
            .HasColumnName("currency")
            .HasColumnType("char(3)")
            .HasDefaultValue(Payment.Currency)
            .IsRequired();

        builder.Property(p => p.InstallmentCount).HasColumnName("installment_count").IsRequired();
        builder.Property(p => p.PlanRevision).HasColumnName("plan_revision").IsRequired();

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasConversion(new WireConverter<PaymentStatus>(s => PaymentStatusValues.ToWire(s), v => PaymentStatusValues.FromWire(v)))
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(p => p.RefundPercentage).HasColumnName("refund_percentage");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(p => p.CancelledAt).HasColumnName("cancelled_at").HasColumnType("timestamptz");
        builder.Property(p => p.SettledAt).HasColumnName("settled_at").HasColumnType("timestamptz");

        // xmin: coluna de sistema do Postgres, muda a cada UPDATE. Não entra na migration.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasIndex(p => p.BookingId).IsUnique().HasDatabaseName("uq_payments_booking_id");
        builder.HasIndex(p => p.OwnerId).HasDatabaseName("ix_payments_owner_id");
        builder.HasIndex(p => new { p.HostId, p.CreatedAt }).HasDatabaseName("ix_payments_host_id_created_at");

        // Fila da manutenção: só os que ainda têm o que fazer.
        builder.HasIndex(p => p.UpdatedAt)
            .HasDatabaseName("ix_payments_updated_at_unsettled")
            .HasFilter("settled_at IS NULL");

        builder.OwnsMany(p => p.Installments, installments =>
        {
            installments.ToTable("payment_installments", table =>
            {
                table.HasCheckConstraint("ck_payment_installments_number", $"number BETWEEN 1 AND {Payment.MaxInstallments}");
                table.HasCheckConstraint("ck_payment_installments_amount_cents", "amount_cents > 0");
                table.HasCheckConstraint("ck_payment_installments_status", $"status IN ({InList(InstallmentStatusValues.All)})");
                table.HasCheckConstraint("ck_payment_installments_method", $"method IS NULL OR method IN ({InList(PaymentMethodValues.All)})");
                table.HasCheckConstraint("ck_payment_installments_refund_status", $"refund_status IN ({InList(RefundStatusValues.All)})");
                table.HasCheckConstraint("ck_payment_installments_payout_status", $"payout_status IN ({InList(PayoutStatusValues.All)})");
                table.HasCheckConstraint("ck_payment_installments_dispute_status", $"dispute_status IN ({InList(DisputeStatusValues.All)})");
                table.HasCheckConstraint("ck_payment_installments_refunded_cents", "refunded_cents BETWEEN 0 AND amount_cents");
                table.HasCheckConstraint("ck_payment_installments_split", "host_payout_cents >= 0 AND platform_fee_cents >= 0 AND host_payout_cents + platform_fee_cents <= amount_cents");
                table.HasCheckConstraint("ck_payment_installments_reversed_cents", "reversed_cents BETWEEN 0 AND host_payout_cents");
            });

            installments.WithOwner().HasForeignKey("PaymentId").HasConstraintName("fk_payment_installments_payments_payment_id");
            installments.Property<PaymentId>("PaymentId")
                .HasColumnName("payment_id")
                .HasConversion(id => id.Value, value => new PaymentId(value));

            installments.HasKey(i => i.Id);
            installments.Property(i => i.Id).HasColumnName("id").ValueGeneratedNever();

            installments.Property(i => i.Number).HasColumnName("number").IsRequired();
            installments.Property(i => i.AmountCents).HasColumnName("amount_cents").IsRequired();
            installments.Property(i => i.DueAt).HasColumnName("due_at").HasColumnType("timestamptz").IsRequired();

            installments.Property(i => i.Status)
                .HasColumnName("status")
                .HasConversion(new WireConverter<InstallmentStatus>(s => InstallmentStatusValues.ToWire(s), v => InstallmentStatusValues.FromWire(v)))
                .HasMaxLength(20)
                .IsRequired();

            installments.Property(i => i.Method)
                .HasColumnName("method")
                .HasConversion(new WireConverter<PaymentMethod>(m => PaymentMethodValues.ToWire(m), v => PaymentMethodValues.FromWire(v)))
                .HasMaxLength(10);

            installments.Property(i => i.StripePaymentIntentId).HasColumnName("stripe_payment_intent_id").HasMaxLength(255);
            installments.Property(i => i.IntentOpen).HasColumnName("intent_open").IsRequired();
            installments.Property(i => i.PaymentAttempts).HasColumnName("payment_attempts").IsRequired();
            installments.Property(i => i.StripeChargeId).HasColumnName("stripe_charge_id").HasMaxLength(255);
            installments.Property(i => i.FailureCode).HasColumnName("failure_code").HasMaxLength(100);
            installments.Property(i => i.PaidAt).HasColumnName("paid_at").HasColumnType("timestamptz");
            installments.Property(i => i.ReminderSentAt).HasColumnName("reminder_sent_at").HasColumnType("timestamptz");
            installments.Property(i => i.OverdueSince).HasColumnName("overdue_since").HasColumnType("timestamptz");

            installments.Property(i => i.RefundStatus)
                .HasColumnName("refund_status")
                .HasConversion(new WireConverter<RefundStatus>(s => RefundStatusValues.ToWire(s), v => RefundStatusValues.FromWire(v)))
                .HasMaxLength(20)
                .IsRequired();

            installments.Property(i => i.RefundedCents).HasColumnName("refunded_cents").IsRequired();
            installments.Property(i => i.StripeRefundId).HasColumnName("stripe_refund_id").HasMaxLength(255);
            installments.Property(i => i.RefundedAt).HasColumnName("refunded_at").HasColumnType("timestamptz");

            installments.Property(i => i.PayoutStatus)
                .HasColumnName("payout_status")
                .HasConversion(new WireConverter<PayoutStatus>(s => PayoutStatusValues.ToWire(s), v => PayoutStatusValues.FromWire(v)))
                .HasMaxLength(20)
                .IsRequired();

            installments.Property(i => i.HostPayoutCents).HasColumnName("host_payout_cents").IsRequired();
            installments.Property(i => i.PlatformFeeCents).HasColumnName("platform_fee_cents").IsRequired();
            installments.Property(i => i.StripeTransferId).HasColumnName("stripe_transfer_id").HasMaxLength(255);
            installments.Property(i => i.TransferredAt).HasColumnName("transferred_at").HasColumnType("timestamptz");
            installments.Property(i => i.PayoutFailureCode).HasColumnName("payout_failure_code").HasMaxLength(100);
            installments.Property(i => i.ReversedCents).HasColumnName("reversed_cents").IsRequired();
            installments.Property(i => i.StripeTransferReversalId).HasColumnName("stripe_transfer_reversal_id").HasMaxLength(255);
            installments.Property(i => i.ReversedAt).HasColumnName("reversed_at").HasColumnType("timestamptz");

            installments.Property(i => i.DisputeStatus)
                .HasColumnName("dispute_status")
                .HasConversion(new WireConverter<DisputeStatus>(s => DisputeStatusValues.ToWire(s), v => DisputeStatusValues.FromWire(v)))
                .HasMaxLength(20)
                .IsRequired();

            installments.Property(i => i.StripeDisputeId).HasColumnName("stripe_dispute_id").HasMaxLength(255);
            installments.Property(i => i.DisputedAt).HasColumnName("disputed_at").HasColumnType("timestamptz");
            installments.Property(i => i.DisputeClosedAt).HasColumnName("dispute_closed_at").HasColumnType("timestamptz");

            installments.HasIndex("PaymentId", nameof(Installment.Number)).IsUnique().HasDatabaseName("uq_payment_installments_payment_id_number");

            // O webhook acha a parcela pela cobrança.
            installments.HasIndex(i => i.StripePaymentIntentId)
                .IsUnique()
                .HasDatabaseName("uq_payment_installments_stripe_payment_intent_id")
                .HasFilter("stripe_payment_intent_id IS NOT NULL");

            // Tutor com parcela em atraso não pede estadia nova.
            installments.HasIndex(i => i.Status).HasDatabaseName("ix_payment_installments_status");

            installments.Ignore(i => i.IsPaid);
            installments.Ignore(i => i.IsOpen);
            installments.Ignore(i => i.IsSettled);
        });

        builder.Navigation(p => p.Installments).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(p => p.HasOverdueInstallment);
        builder.Ignore(p => p.CanChangePlan);
        builder.Ignore(p => p.PaidCents);
        builder.Ignore(p => p.OpenIntentIds);
        builder.Ignore(p => p.DomainEvents);
    }

    private static string InList(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"'{v}'"));
}
