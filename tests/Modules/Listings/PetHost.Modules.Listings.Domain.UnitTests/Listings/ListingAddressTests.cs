using FluentAssertions;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Listings.Domain.UnitTests.Listings;

public sealed class ListingAddressTests
{
    private readonly ListingBuilder _builder = new();

    [Fact]
    public void Create_Should_KeepOnlyZipDigitsAndUppercaseState_When_AddressIsValid()
    {
        // Arrange
        var data = _builder.WithAddress(a => a with { ZipCode = "87.020-000", State = "pr", Complement = " " }).BuildAddressData();

        // Act
        var result = ListingAddress.Create(data);

        // Assert
        result.Value!.ZipCode.Should().Be("87020000");
        result.Value.State.Should().Be("PR");
        result.Value.Complement.Should().BeNull();
    }

    [Fact]
    public void Create_Should_ReturnEveryAddressError_When_AddressIsMissing()
    {
        // Act
        var result = ListingAddress.Create(null);

        // Assert
        result.Errors.Should().BeEquivalentTo(
        [
            ListingsErrors.ZipCodeInvalid, ListingsErrors.StreetRequired, ListingsErrors.NumberRequired,
            ListingsErrors.NeighborhoodRequired, ListingsErrors.CityRequired, ListingsErrors.StateInvalid,
        ]);
    }

    [Theory]
    [InlineData("8702000")]
    [InlineData("87020-00a")]
    [InlineData("00000000")]
    public void Create_Should_RejectZipCode_When_ItIsNotEightDigits(string zipCode)
    {
        // Arrange
        var data = _builder.WithAddress(a => a with { ZipCode = zipCode }).BuildAddressData();

        // Act
        var result = ListingAddress.Create(data);

        // Assert
        result.Errors.Should().ContainSingle().Which.Should().Be(ListingsErrors.ZipCodeInvalid);
    }

    [Fact]
    public void Create_Should_RejectState_When_ItDoesNotExist()
    {
        // Arrange
        var data = _builder.WithAddress(a => a with { State = "XX" }).BuildAddressData();

        // Act
        var result = ListingAddress.Create(data);

        // Assert
        result.Errors.Should().ContainSingle().Which.Should().Be(ListingsErrors.StateInvalid);
    }

    [Fact]
    public void Create_Should_RejectTextsAboveTheLimit()
    {
        // Arrange
        var data = _builder.WithAddress(a => a with
        {
            Street = new string('a', ListingAddress.StreetMaxLength + 1),
            Number = new string('1', ListingAddress.NumberMaxLength + 1),
            Complement = new string('a', ListingAddress.ComplementMaxLength + 1),
            Neighborhood = new string('a', ListingAddress.NeighborhoodMaxLength + 1),
            City = new string('a', ListingAddress.CityMaxLength + 1),
        }).BuildAddressData();

        // Act
        var result = ListingAddress.Create(data);

        // Assert
        result.Errors.Should().BeEquivalentTo(
        [
            ListingsErrors.StreetTooLong, ListingsErrors.NumberTooLong, ListingsErrors.ComplementTooLong,
            ListingsErrors.NeighborhoodTooLong, ListingsErrors.CityTooLong,
        ]);
    }

    [Fact]
    public void Equality_Should_IgnoreZipMask_When_AddressesAreTheSame()
    {
        // Arrange
        var masked = ListingAddress.Create(_builder.WithAddress(a => a with { ZipCode = "87020-000" }).BuildAddressData()).Value;
        var digits = ListingAddress.Create(_builder.WithAddress(a => a with { ZipCode = "87020000" }).BuildAddressData()).Value;

        // Assert
        masked.Should().Be(digits);
    }
}
