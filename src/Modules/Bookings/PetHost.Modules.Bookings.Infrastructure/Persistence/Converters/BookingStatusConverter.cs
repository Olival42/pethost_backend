using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Bookings.Domain.Bookings;

namespace PetHost.Modules.Bookings.Infrastructure.Persistence.Converters;

/// <summary><see cref="BookingStatus"/> ↔ texto (<c>requested</c>, <c>in_progress</c>...): enum como string (§11).</summary>
internal sealed class BookingStatusConverter : ValueConverter<BookingStatus, string>
{
    public BookingStatusConverter()
        : base(status => BookingStatusValues.ToWire(status), value => Parse(value))
    {
    }

    private static BookingStatus Parse(string value) =>
        BookingStatusValues.TryParse(value, out var status)
            ? status
            : throw new InvalidOperationException($"Unknown booking status '{value}' in the database.");
}
