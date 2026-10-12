using FluentAssertions;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using Xunit;

namespace PetHost.Modules.Listings.Domain.UnitTests.Listings;

public sealed class GeoPointTests
{
    [Fact]
    public void Create_Should_RoundToSixDecimals_When_CoordinatesAreValid()
    {
        // Act
        var point = GeoPoint.Create(-23.4252694m, -51.9386045m);

        // Assert
        point.Value!.Latitude.Should().Be(-23.425269m);
        point.Value.Longitude.Should().Be(-51.938605m);
    }

    [Fact]
    public void Create_Should_ReturnBothErrors_When_CoordinatesAreOutOfRange()
    {
        // Act
        var point = GeoPoint.Create(91m, -181m);

        // Assert
        point.Errors.Should().BeEquivalentTo([ListingsErrors.LatitudeOutOfRange, ListingsErrors.LongitudeOutOfRange]);
    }

    [Fact]
    public void DistanceKmTo_Should_BeZero_When_PointsAreTheSame()
    {
        // Arrange
        var point = GeoPoint.Create(-23.42m, -51.93m).Value!;

        // Act & Assert
        point.DistanceKmTo(point).Should().Be(0);
    }

    [Fact]
    public void DistanceKmTo_Should_MatchTheRealDistance_When_GoingFromMaringaToLondrina()
    {
        // Arrange — centros de Maringá e Londrina: cerca de 80 km em linha reta.
        var maringa = GeoPoint.Create(-23.425269m, -51.938604m).Value!;
        var londrina = GeoPoint.Create(-23.310410m, -51.162800m).Value!;

        // Act
        var distance = maringa.DistanceKmTo(londrina);

        // Assert
        distance.Should().BeApproximately(80, 2);
    }
}
