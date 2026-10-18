using FluentAssertions;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Listings.Domain.UnitTests.Listings;

public sealed class StayWindowTests
{
    private static readonly DateOnly Today = new(2026, 10, 11);

    [Fact]
    public void Parse_Should_ReturnNull_When_NoDatesAreSent()
    {
        // Act
        var result = StayWindow.Parse(null, " ", Today);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public void Parse_Should_ReturnTheWindow_When_DatesAreValid()
    {
        // Act
        var result = StayWindow.Parse("2026-10-20", "2026-10-25", Today);

        // Assert
        result.Value.Should().Be(new StayWindow(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 25)));
    }

    [Theory]
    [InlineData("2026-10-20", null, "checkOut")]
    [InlineData(null, "2026-10-25", "checkIn")]
    public void Parse_Should_RequireBothDates(string? checkIn, string? checkOut, string field)
    {
        // Act
        var result = StayWindow.Parse(checkIn, checkOut, Today);

        // Assert
        result.FirstError!.Field.Should().Be(field);
    }

    [Theory]
    [InlineData("2026-10-10", "2026-10-12", "CheckInFilterInThePast")]
    [InlineData("2027-10-20", "2027-10-22", "CheckInFilterTooFarAhead")]
    [InlineData("2026-10-20", "2026-10-20", "CheckOutFilterNotAfterCheckIn")]
    [InlineData("2026-10-20", "2026-11-20", "StayFilterTooLong")]
    [InlineData("20/10/2026", "2026-10-25", "CheckInFilterInvalid")]
    public void Parse_Should_ApplyTheBookingRules(string checkIn, string checkOut, string error)
    {
        // Act
        var result = StayWindow.Parse(checkIn, checkOut, Today);

        // Assert
        result.FirstError.Should().Be(typeof(ListingsErrors).GetField(error)!.GetValue(null));
    }

    [Fact]
    public void RequiredWeekdays_Should_IncludeCheckOutDay()
    {
        // Arrange: sábado 24 a segunda 26.
        var window = new StayWindow(new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 26));

        // Act
        var days = window.RequiredWeekdays();

        // Assert
        days.Should().Be(Weekdays.Saturday | Weekdays.Sunday | Weekdays.Monday);
    }

    [Fact]
    public void Today_Should_UseBrasiliaTime()
    {
        // Act: 02:00 UTC de 20/10 = 23:00 de 19/10 em Brasília.
        var today = StayWindow.Today(new DateTimeOffset(2026, 10, 20, 2, 0, 0, TimeSpan.Zero));

        // Assert
        today.Should().Be(new DateOnly(2026, 10, 19));
    }
}

public sealed class ListingBadgesTests
{
    private static readonly ListingAddressData ZonaSete = new("87020-000", "Rua X", "1", null, "zona 7", "MARINGA", "PR");

    [Fact]
    public void For_Should_GiveEveryBadge_When_AllRulesMatch()
    {
        // Act
        var badges = ListingBadges.For(new ListingBuilder().Build(), ZonaSete, completedStays: 5, TimeSpan.FromMinutes(119), new ListingRating(4.5m, 3));

        // Assert
        badges.Should().Equal(ListingBadges.All);
    }

    [Theory]
    [InlineData(4.49, 10)]
    [InlineData(5.0, 2)]
    public void For_Should_SkipWellRated_When_TheRatingIsBelowTheRule(double average, int count)
    {
        // Act
        var badges = ListingBadges.For(new ListingBuilder().Build(), null, 0, null, new ListingRating((decimal)average, count));

        // Assert
        badges.Should().NotContain(ListingBadges.WellRated);
    }

    [Fact]
    public void For_Should_GiveWellRated_When_TheRatingIs45WithThreeReviews()
    {
        // Act
        var badges = ListingBadges.For(new ListingBuilder().Build(), null, 0, null, new ListingRating(4.5m, 3));

        // Assert
        badges.Should().Contain(ListingBadges.WellRated);
    }

    [Fact]
    public void For_Should_GiveNone_When_NoRuleMatches()
    {
        // Arrange
        var listing = new ListingBuilder().WithDetails(d => d with { HasYard = false }).Build();
        var elsewhere = ZonaSete with { Neighborhood = "Zona 5" };

        // Act
        var badges = ListingBadges.For(listing, elsewhere, completedStays: 4, TimeSpan.FromHours(2));

        // Assert
        badges.Should().BeEmpty();
    }

    [Fact]
    public void For_Should_SkipNeighbor_When_ViewerHasNoAddress()
    {
        // Act
        var badges = ListingBadges.For(new ListingBuilder().Build(), null, completedStays: 0, averageResponseTime: null);

        // Assert
        badges.Should().Equal(ListingBadges.Yard);
    }

    [Fact]
    public void For_Should_SkipNeighbor_When_SameNeighborhoodNameIsInAnotherCity()
    {
        // Act
        var badges = ListingBadges.For(new ListingBuilder().Build(), ZonaSete with { City = "Londrina" }, 0, null);

        // Assert
        badges.Should().NotContain(ListingBadges.Neighbor);
    }
}
