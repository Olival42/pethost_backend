using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Bookings.Domain.Bookings;

namespace PetHost.Modules.Bookings.Infrastructure.Persistence.Converters;

/// <summary><see cref="BookingId"/> ↔ <c>uuid</c>.</summary>
internal sealed class BookingIdConverter : ValueConverter<BookingId, Guid>
{
    public BookingIdConverter()
        : base(id => id.Value, value => new BookingId(value))
    {
    }
}
