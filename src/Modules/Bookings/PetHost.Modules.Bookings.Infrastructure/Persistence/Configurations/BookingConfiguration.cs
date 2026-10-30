using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Infrastructure.Persistence.Converters;

namespace PetHost.Modules.Bookings.Infrastructure.Persistence.Configurations;

/// <summary>
/// Mapeia <see cref="Booking"/> nas tabelas <c>booking.bookings</c> e <c>booking.booking_pets</c>.
/// <c>owner_id</c>, <c>listing_id</c>, <c>host_id</c> e <c>booking_pets.pet_id</c> são <b>FKs
/// lógicas</b>, sem constraint física: cada módulo tem o seu schema e não há restrição nem JOIN
/// entre schemas (§5, §11).
/// </summary>
/// <remarks>
/// Os CHECKs repetem no banco as regras do domínio. Concorrência otimista pelo <c>xmin</c>: o
/// tutor cancelando enquanto o anfitrião marca a chegada não grava os dois. Os índices que usam
/// as datas (colunas do tipo complexo <see cref="StayPeriod"/>) são criados em SQL na migration.
/// </remarks>
internal sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings", table =>
        {
            table.HasCheckConstraint("ck_bookings_period", "check_out_date > check_in_date");
            table.HasCheckConstraint("ck_bookings_pet_count", $"pet_count BETWEEN 1 AND {Booking.MaxPets}");
            table.HasCheckConstraint("ck_bookings_status", $"status IN ({InList(BookingStatusValues.All)})");
            table.HasCheckConstraint("ck_bookings_cancelled_by", $"cancelled_by IS NULL OR cancelled_by IN ({InList(BookingPartyValues.All)})");
            table.HasCheckConstraint("ck_bookings_refund_percentage", "refund_percentage IS NULL OR refund_percentage BETWEEN 0 AND 100");
            table.HasCheckConstraint("ck_bookings_amounts", "nightly_rate >= 0 AND total_price >= 0");
        });

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id)
            .HasColumnName("id")
            .HasConversion<BookingIdConverter>()
            .ValueGeneratedNever();

        builder.Property(b => b.OwnerId).HasColumnName("owner_id").IsRequired();
        builder.Property(b => b.ListingId).HasColumnName("listing_id").IsRequired();
        builder.Property(b => b.HostId).HasColumnName("host_id").IsRequired();

        builder.ComplexProperty(b => b.Period, period =>
        {
            period.Property(p => p.CheckIn).HasColumnName("check_in_date").HasColumnType("date").IsRequired();
            period.Property(p => p.CheckOut).HasColumnName("check_out_date").HasColumnType("date").IsRequired();
        });

        builder.Property(b => b.PetCount).HasColumnName("pet_count").IsRequired();

        // numeric, nunca float (§11). Em reais, como a diária do cantinho.
        builder.Property(b => b.NightlyRate).HasColumnName("nightly_rate").HasPrecision(10, 2).IsRequired();
        builder.Property(b => b.TotalPrice).HasColumnName("total_price").HasPrecision(12, 2).IsRequired();

        builder.Property(b => b.Status)
            .HasColumnName("status")
            .HasConversion<BookingStatusConverter>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(b => b.RequestedAt).HasColumnName("requested_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(b => b.RespondedAt).HasColumnName("responded_at").HasColumnType("timestamptz");
        builder.Property(b => b.ConfirmedAt).HasColumnName("confirmed_at").HasColumnType("timestamptz");
        builder.Property(b => b.CheckedInAt).HasColumnName("checked_in_at").HasColumnType("timestamptz");
        builder.Property(b => b.CheckedOutAt).HasColumnName("checked_out_at").HasColumnType("timestamptz");
        builder.Property(b => b.ExpiredAt).HasColumnName("expired_at").HasColumnType("timestamptz");
        builder.Property(b => b.CheckInRemindedAt).HasColumnName("check_in_reminded_at").HasColumnType("timestamptz");
        builder.Property(b => b.CheckOutRemindedAt).HasColumnName("check_out_reminded_at").HasColumnType("timestamptz");
        builder.Property(b => b.CareTermsAcceptedAt).HasColumnName("care_terms_accepted_at").HasColumnType("timestamptz");
        builder.Property(b => b.ResponseRemindedAt).HasColumnName("response_reminded_at").HasColumnType("timestamptz");
        builder.Property(b => b.PaymentRemindedAt).HasColumnName("payment_reminded_at").HasColumnType("timestamptz");
        builder.Property(b => b.CancelledAt).HasColumnName("cancelled_at").HasColumnType("timestamptz");

        builder.Property(b => b.CancelledBy)
            .HasColumnName("cancelled_by")
            .HasConversion<BookingPartyConverter>()
            .HasMaxLength(10);

        builder.Property(b => b.CancellationReason).HasColumnName("cancellation_reason").HasMaxLength(Booking.ReasonMaxLength);
        builder.Property(b => b.DeclineReason).HasColumnName("decline_reason").HasMaxLength(Booking.ReasonMaxLength);
        builder.Property(b => b.RefundPercentage).HasColumnName("refund_percentage");

        builder.Property(b => b.CreatedAt).HasColumnName("created_at").HasColumnType("timestamptz").IsRequired();
        builder.Property(b => b.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamptz").IsRequired();

        // xmin: coluna de sistema do Postgres, muda a cada UPDATE. Não entra na migration.
        builder.Property<uint>("Version").IsRowVersion();

        // Pedidos sem resposta, para a tarefa de expiração.
        builder.HasIndex(b => new { b.Status, b.RequestedAt }).HasDatabaseName("ix_bookings_status_requested_at");

        builder.OwnsMany(b => b.Pets, pets =>
        {
            pets.ToTable("booking_pets");
            pets.WithOwner().HasForeignKey("BookingId").HasConstraintName("fk_booking_pets_bookings_booking_id");
            pets.Property<BookingId>("BookingId").HasColumnName("booking_id").HasConversion<BookingIdConverter>();
            pets.Property(p => p.PetId).HasColumnName("pet_id").IsRequired();
            pets.HasKey("BookingId", nameof(BookingPet.PetId));

            // "Este pet já está em outra reserva no período?"
            pets.HasIndex(p => p.PetId).HasDatabaseName("ix_booking_pets_pet_id");
        });

        builder.Navigation(b => b.Pets).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(b => b.ResponseDeadline);
        builder.Ignore(b => b.OccupiesCapacity);
        builder.Ignore(b => b.RevealsContact);
        builder.Ignore(b => b.DomainEvents);
    }

    private static string InList(IEnumerable<string> values) => string.Join(", ", values.Select(v => $"'{v}'"));
}
