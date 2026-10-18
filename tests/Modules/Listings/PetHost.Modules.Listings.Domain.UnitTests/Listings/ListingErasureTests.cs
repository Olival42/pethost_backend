using FluentAssertions;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Listings.Domain.UnitTests.Listings;

/// <summary>A conta do anfitrião foi excluída (LGPD): o cantinho sai do ar sem fotos nem endereço exato.</summary>
public sealed class ListingErasureTests
{
    private static readonly DateTimeOffset Now = ListingBuilder.DefaultNow;
    private static readonly DateTimeOffset Later = Now.AddDays(3);

    private readonly ListingBuilder _builder = new();

    private static string Hash(char seed) => new(seed, 64);

    [Fact]
    public void Erase_Should_RemoveEveryPhotoAndReturnTheUrls_When_ListingHasPhotos()
    {
        // Arrange
        var listing = _builder.Build();
        listing.AddPhoto("https://cdn.pethost.test/1.jpg", Hash('1'), Now);
        listing.AddPhoto("https://cdn.pethost.test/2.jpg", Hash('2'), Now);

        // Act
        var urls = listing.Erase(Later);

        // Assert
        urls.Should().BeEquivalentTo("https://cdn.pethost.test/1.jpg", "https://cdn.pethost.test/2.jpg");
        listing.Photos.Should().BeEmpty();
        listing.IsActive.Should().BeFalse();
        listing.DeactivatedAt.Should().Be(Later);
    }

    [Fact]
    public void Erase_Should_KeepOnlyNeighborhoodCityAndState_When_Called()
    {
        // Arrange
        var listing = _builder.Build();
        var before = listing.Address;

        // Act
        listing.Erase(Later);

        // Assert
        listing.Address.ZipCode.Should().Be(ListingAddress.RedactedZipCode);
        listing.Address.Street.Should().Be(ListingAddress.RedactedStreet);
        listing.Address.Number.Should().Be("S/N");
        listing.Address.Complement.Should().BeNull();
        listing.Address.Neighborhood.Should().Be(before.Neighborhood);
        listing.Address.City.Should().Be(before.City);
        listing.Address.State.Should().Be(before.State);
        listing.UpdatedAt.Should().Be(Later);
    }

    [Fact]
    public void Erase_Should_ReplaceTheExactPointWithTheApproximateOne_When_Called()
    {
        // Arrange
        var listing = _builder.Build();
        var approximate = listing.ApproximateLocation;

        // Act
        listing.Erase(Later);

        // Assert
        listing.Location.Should().Be(approximate);
        listing.ApproximateLocation.Should().Be(approximate);
    }

    [Fact]
    public void Erase_Should_KeepTheDetails_When_Called()
    {
        // Arrange
        var listing = _builder.Build();
        var details = listing.Details;

        // Act
        listing.Erase(Later);

        // Assert
        listing.Details.Should().Be(details, "reservas e avaliações antigas mostram o cantinho");
    }

    [Fact]
    public void Erase_Should_ChangeNothing_When_AlreadyErased()
    {
        // Arrange
        var listing = _builder.Build();
        listing.Erase(Later);

        // Act
        var urls = listing.Erase(Later.AddDays(1));

        // Assert
        urls.Should().BeEmpty();
        listing.UpdatedAt.Should().Be(Later);
    }
}
