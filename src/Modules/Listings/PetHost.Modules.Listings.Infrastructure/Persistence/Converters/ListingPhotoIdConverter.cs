using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Infrastructure.Persistence.Converters;

/// <summary><see cref="ListingPhotoId"/> ↔ <c>uuid</c>.</summary>
internal sealed class ListingPhotoIdConverter : ValueConverter<ListingPhotoId, Guid>
{
    public ListingPhotoIdConverter()
        : base(id => id.Value, value => new ListingPhotoId(value))
    {
    }
}
