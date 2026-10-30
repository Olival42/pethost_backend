using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Infrastructure.Persistence.Converters;

/// <summary><see cref="AvailabilityBlockId"/> ↔ <c>uuid</c>.</summary>
internal sealed class AvailabilityBlockIdConverter : ValueConverter<AvailabilityBlockId, Guid>
{
    public AvailabilityBlockIdConverter()
        : base(id => id.Value, value => new AvailabilityBlockId(value))
    {
    }
}
