using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Infrastructure.Persistence.Converters;

/// <summary><see cref="ListingId"/> ↔ <c>uuid</c>.</summary>
internal sealed class ListingIdConverter : ValueConverter<ListingId, Guid>
{
    public ListingIdConverter()
        : base(id => id.Value, value => new ListingId(value))
    {
    }
}
