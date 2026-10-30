using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Infrastructure.Persistence.Converters;

/// <summary><see cref="HomeType"/> ↔ texto (<c>house</c>, <c>apartment</c>): enum como string (§11).</summary>
internal sealed class HomeTypeConverter : ValueConverter<HomeType, string>
{
    public HomeTypeConverter()
        : base(homeType => HomeTypeValues.ToWire(homeType), value => FromDatabase(value))
    {
    }

    private static HomeType FromDatabase(string value) =>
        HomeTypeValues.TryParse(value, out var homeType)
            ? homeType
            : throw new InvalidOperationException($"The stored home type '{value}' is unknown. Database integrity issue.");
}
