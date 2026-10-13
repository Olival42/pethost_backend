using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Bookings.Domain.Bookings;

namespace PetHost.Modules.Bookings.Infrastructure.Persistence.Converters;

/// <summary><see cref="BookingParty"/> ↔ texto (<c>owner</c>, <c>host</c>).</summary>
internal sealed class BookingPartyConverter : ValueConverter<BookingParty, string>
{
    public BookingPartyConverter()
        : base(party => BookingPartyValues.ToWire(party), value => BookingPartyValues.FromWire(value))
    {
    }
}
