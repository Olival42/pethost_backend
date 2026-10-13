namespace PetHost.Modules.Bookings.Domain.Bookings;

/// <summary>Identidade de <see cref="Booking"/>. UUID v7 gerado pelo domínio.</summary>
public readonly record struct BookingId(Guid Value)
{
    public static BookingId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
