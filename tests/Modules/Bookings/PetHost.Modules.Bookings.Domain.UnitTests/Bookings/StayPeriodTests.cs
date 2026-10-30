using FluentAssertions;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using Xunit;

namespace PetHost.Modules.Bookings.Domain.UnitTests.Bookings;

public sealed class StayPeriodTests
{
    private static readonly DateOnly Today = new(2026, 10, 11);

    [Fact]
    public void Create_Should_ReturnPeriodWithNights_When_DatesAreValid()
    {
        // Act
        var period = StayPeriod.Create(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 25), Today);

        // Assert
        period.IsSuccess.Should().BeTrue();
        period.Value!.Nights.Should().Be(5);
    }

    [Fact]
    public void Create_Should_AcceptCheckInToday()
    {
        // Act
        var period = StayPeriod.Create(Today, Today.AddDays(1), Today);

        // Assert
        period.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_Should_ReturnValidationError_When_CheckInIsInThePast()
    {
        // Act
        var period = StayPeriod.Create(Today.AddDays(-1), Today.AddDays(2), Today);

        // Assert
        period.Errors.Should().ContainSingle().Which.Should().Be(BookingsErrors.CheckInInThePast);
    }

    [Fact]
    public void Create_Should_ReturnValidationError_When_CheckInIsTooFarAhead()
    {
        // Act
        var checkIn = Today.AddDays(StayPeriod.MaxDaysAhead + 1);
        var period = StayPeriod.Create(checkIn, checkIn.AddDays(2), Today);

        // Assert
        period.Errors.Should().ContainSingle().Which.Should().Be(BookingsErrors.CheckInTooFarAhead);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Create_Should_ReturnValidationError_When_CheckOutIsNotAfterCheckIn(int nights)
    {
        // Act
        var checkIn = Today.AddDays(5);
        var period = StayPeriod.Create(checkIn, checkIn.AddDays(nights), Today);

        // Assert
        period.Errors.Should().ContainSingle().Which.Should().Be(BookingsErrors.CheckOutNotAfterCheckIn);
    }

    [Fact]
    public void Create_Should_ReturnValidationError_When_StayHasMoreThanMaxNights()
    {
        // Act
        var checkIn = Today.AddDays(5);
        var period = StayPeriod.Create(checkIn, checkIn.AddDays(StayPeriod.MaxNights + 1), Today);

        // Assert
        period.Errors.Should().ContainSingle().Which.Should().Be(BookingsErrors.StayTooLong);
    }

    [Fact]
    public void Create_Should_AcceptExactlyMaxNights()
    {
        // Act
        var checkIn = Today.AddDays(5);
        var period = StayPeriod.Create(checkIn, checkIn.AddDays(StayPeriod.MaxNights), Today);

        // Assert
        period.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Create_Should_ReturnBothErrors_When_CheckInIsPastAndCheckOutIsBeforeIt()
    {
        // Act
        var period = StayPeriod.Create(Today.AddDays(-1), Today.AddDays(-2), Today);

        // Assert
        period.Errors.Should().BeEquivalentTo([BookingsErrors.CheckInInThePast, BookingsErrors.CheckOutNotAfterCheckIn]);
    }

    [Fact]
    public void EachNight_Should_ListFromCheckInUntilTheDayBeforeCheckOut()
    {
        // Arrange
        var period = StayPeriod.Create(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 23), Today).Value!;

        // Act
        var nights = period.EachNight();

        // Assert
        nights.Should().Equal(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 22));
    }

    [Fact]
    public void EachDay_Should_IncludeCheckOutDay()
    {
        // Arrange
        var period = StayPeriod.Create(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 22), Today).Value!;

        // Act
        var days = period.EachDay();

        // Assert
        days.Should().Equal(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 22));
    }

    [Theory]
    [InlineData(20, 25, 24, 27, true)]
    [InlineData(20, 25, 15, 21, true)]
    [InlineData(20, 25, 21, 23, true)]
    [InlineData(20, 25, 25, 28, false)]
    [InlineData(20, 25, 15, 20, false)]
    public void Overlaps_Should_CompareNights(int checkIn, int checkOut, int otherCheckIn, int otherCheckOut, bool expected)
    {
        // Arrange
        var period = StayPeriod.Create(new DateOnly(2026, 10, checkIn), new DateOnly(2026, 10, checkOut), Today).Value!;
        var other = StayPeriod.Create(new DateOnly(2026, 10, otherCheckIn), new DateOnly(2026, 10, otherCheckOut), Today).Value!;

        // Act
        var overlaps = period.Overlaps(other);

        // Assert
        overlaps.Should().Be(expected);
    }

    [Theory]
    [InlineData("2026-10-20", true)]
    [InlineData(" 2026-10-20 ", true)]
    [InlineData("20/10/2026", false)]
    [InlineData("2026-02-30", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParseDate_Should_AcceptOnlyIsoDates(string? text, bool expected)
    {
        // Act
        var parsed = StayPeriod.TryParseDate(text, out _);

        // Assert
        parsed.Should().Be(expected);
    }

    [Fact]
    public void Equality_Should_CompareDates()
    {
        // Arrange
        var first = StayPeriod.Create(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 25), Today).Value!;
        var second = StayPeriod.Create(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 25), Today).Value!;

        // Assert
        first.Should().Be(second);
    }
}
