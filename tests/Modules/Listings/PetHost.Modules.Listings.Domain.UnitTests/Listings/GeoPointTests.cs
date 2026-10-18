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

    [Fact]
    public void Approximate_Should_StayInsideTheMapCircle_And_RoundToThreeDecimals()
    {
        // Arrange
        var home = GeoPoint.Create(-23.425269m, -51.938604m).Value!;
        var random = new Random(42);

        for (var i = 0; i < 1000; i++)
        {
            // Act
            var approximate = home.Approximate(random);

            // Assert
            home.DistanceKmTo(approximate).Should().BeLessThan(GeoPoint.ApproximateRadiusMeters / 1000.0);
            approximate.Latitude.Scale.Should().BeLessThanOrEqualTo(GeoPoint.ApproximateDecimals);
            approximate.Longitude.Scale.Should().BeLessThanOrEqualTo(GeoPoint.ApproximateDecimals);
        }
    }

    [Fact]
    public void Approximate_Should_MoveThePoint_When_TheDrawIsNotZero()
    {
        // Arrange
        var home = GeoPoint.Create(-23.425269m, -51.938604m).Value!;
        var random = new Random(7);

        // Act
        var draws = Enumerable.Range(0, 200).Select(_ => home.Approximate(random)).ToList();

        // Assert — espalha em volta: não fica no ponto nem sempre no mesmo lugar.
        draws.Should().NotContain(home);
        // Num disco de 0,4 km cabem umas 50 células de 0,001°.
        draws.Distinct().Count().Should().BeGreaterThan(30);
        draws.Average(p => home.DistanceKmTo(p)).Should().BeGreaterThan(0.15);
    }

    [Fact]
    public void Approximate_Should_StayInRange_When_PointIsAtTheEdgeOfTheMap()
    {
        // Arrange
        var corner = GeoPoint.Create(90m, 180m).Value!;

        // Act
        var approximate = corner.Approximate(new Random(1));

        // Assert
        approximate.Latitude.Should().BeInRange(-90m, 90m);
        approximate.Longitude.Should().BeInRange(-180m, 180m);
    }
}
