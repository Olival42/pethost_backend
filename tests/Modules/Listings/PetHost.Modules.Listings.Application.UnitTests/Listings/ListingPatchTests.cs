using FluentAssertions;
using PetHost.Modules.Listings.Application.Listings.UpdateListing;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Listings.Application.UnitTests.Listings;

public sealed class ListingPatchTests
{
    private readonly ListingBuilder _builder = new();

    [Fact]
    public void ApplyDetails_Should_KeepCurrentValues_When_FieldsAreNull()
    {
        // Arrange
        var current = _builder.BuildDetailsData();
        var patch = new UpdateListingCommand(Guid.Empty, Guid.Empty, null, null, null, null, null, null, null, null, null, null, null, null, null, "", null, null);

        // Act
        var merged = ListingPatch.ApplyDetails(current, patch);

        // Assert
        merged.Should().BeEquivalentTo(current with { HouseRules = "" });
    }

    [Fact]
    public void ApplyAddress_Should_ReturnCurrent_When_AddressIsNotSent()
    {
        // Arrange
        var current = _builder.BuildAddressData();

        // Act & Assert
        ListingPatch.ApplyAddress(current, null).Should().Be(current);
    }

    [Fact]
    public void ChangedFields_Should_ListOnlyWhatChanged_ByName()
    {
        // Arrange
        var before = _builder.BuildDetails().ToData();
        var after = new ListingBuilder()
            .WithDetails(d => d with { AcceptedSpecies = ["dog"], Capacity = 4 })
            .BuildDetails()
            .ToData();
        var address = _builder.BuildAddress().ToData();

        // Act
        var changed = ListingPatch.ChangedFields(before, after, address, address with { City = "Sarandi" });

        // Assert
        changed.Should().Equal("acceptedSpecies", "capacity", "address.city");
    }
}
