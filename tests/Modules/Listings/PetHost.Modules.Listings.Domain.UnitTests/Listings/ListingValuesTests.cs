using FluentAssertions;
using PetHost.Modules.Listings.Domain.Listings;
using Xunit;

namespace PetHost.Modules.Listings.Domain.UnitTests.Listings;

public sealed class ListingValuesTests
{
    [Fact]
    public void AcceptedSpecies_Should_RoundTripEveryValue()
    {
        // Act
        var parsed = AcceptedSpeciesValues.TryParse(AcceptedSpeciesValues.All, out var species);

        // Assert
        parsed.Should().BeTrue();
        species.Should().Be(AcceptedSpeciesValues.Every);
        AcceptedSpeciesValues.ToWire(species).Should().Equal(AcceptedSpeciesValues.All);
    }

    [Fact]
    public void AcceptedSpecies_Should_Fail_When_AValueIsUnknown()
    {
        // Act & Assert
        AcceptedSpeciesValues.TryParse(["dog", null], out _).Should().BeFalse();
    }

    [Fact]
    public void Weekdays_Should_ListFromMondayToSunday()
    {
        // Act & Assert
        WeekdaysValues.ToWire(Weekdays.Sunday | Weekdays.Monday | Weekdays.Friday).Should().Equal("monday", "friday", "sunday");
    }

    [Theory]
    [InlineData(DayOfWeek.Monday, true)]
    [InlineData(DayOfWeek.Sunday, false)]
    public void Weekdays_Should_TellWhetherADayIsIncluded(DayOfWeek day, bool included)
    {
        // Act & Assert
        WeekdaysValues.Includes(Weekdays.Monday | Weekdays.Tuesday, day).Should().Be(included);
    }

    [Fact]
    public void Weekdays_Should_CoverTheWholeWeek_When_EveryDayIsParsed()
    {
        // Act
        var parsed = WeekdaysValues.TryParse(WeekdaysValues.All, out var days);

        // Assert
        parsed.Should().BeTrue();
        days.Should().Be(WeekdaysValues.Every);
        ((int)days).Should().Be(127);
    }

    [Fact]
    public void HomeType_Should_ParseIgnoringCase()
    {
        // Act & Assert
        HomeTypeValues.TryParse(" Apartment ", out var homeType).Should().BeTrue();
        homeType.Should().Be(HomeType.Apartment);
        HomeTypeValues.ToWire(HomeType.House).Should().Be("house");
    }
}
