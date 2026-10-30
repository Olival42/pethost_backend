using FluentAssertions;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Bookings.Domain.UnitTests.Bookings;

public sealed class BookableListingTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("small", true)]
    [InlineData("medium", true)]
    [InlineData("large", false)]
    [InlineData("giant", false)]
    public void AcceptsSize_Should_FollowTheListingSizes(string? size, bool expected)
    {
        // Act
        var accepts = BookingBuilder.Listing().AcceptsSize(size);

        // Assert
        accepts.Should().Be(expected);
    }

    [Fact]
    public void FirstUnavailableDay_Should_ReturnNull_When_ListingReceivesEveryDayOfTheStay()
    {
        // Act
        var day = BookingBuilder.Listing().FirstUnavailableDay(BookingBuilder.Period());

        // Assert
        day.Should().BeNull();
    }

    [Fact]
    public void FirstUnavailableDay_Should_ReturnTheFirstClosedDay_IncludingCheckOutDay()
    {
        // Arrange: 20/10/2026 é terça; a estadia vai até sexta (23), e o cantinho não recebe na sexta.
        var listing = BookingBuilder.Listing() with
        {
            AvailableWeekdays = [DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday],
        };
        var period = BookingBuilder.Period(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 23));

        // Act
        var day = listing.FirstUnavailableDay(period);

        // Assert
        day.Should().Be(new DateOnly(2026, 10, 23));
    }
}

public sealed class ListingOccupancyTests
{
    [Fact]
    public void EnsureFits_Should_Succeed_When_ListingIsEmpty()
    {
        // Act
        var result = ListingOccupancy.Empty.EnsureFits(BookingBuilder.Period(), 3, capacity: 3);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void EnsureFits_Should_ReturnCapacityExceededWithTheFirstFullNight_When_ConfirmedStaysFillTheListing()
    {
        // Arrange: 2 pets confirmados de 22 a 24; o novo pedido (20 a 25) quer mais 2 numa casa de 3.
        var confirmed = new BookingBuilder()
            .WithPets(BookingBuilder.Pipoca(), BookingBuilder.Thor())
            .WithPeriod(new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 24))
            .BuildConfirmed();

        // Act
        var result = ListingOccupancy.Of([confirmed]).EnsureFits(BookingBuilder.Period(), 2, capacity: 3);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.CapacityExceeded(new DateOnly(2026, 10, 22)));
    }

    [Fact]
    public void EnsureFits_Should_IgnoreBookingsThatDoNotOccupyCapacity()
    {
        // Arrange: pedido ainda Solicitado não segura vaga.
        var requested = new BookingBuilder().WithPets(BookingBuilder.Pipoca(), BookingBuilder.Thor()).Build();

        // Act
        var result = ListingOccupancy.Of([requested]).EnsureFits(BookingBuilder.Period(), 3, capacity: 3);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void EnsureFits_Should_Succeed_When_OneStayEndsTheDayTheOtherBegins()
    {
        // Arrange: casa de 1 pet; um sai no dia 20, o outro chega no dia 20.
        var leaving = new BookingBuilder()
            .WithListing(l => l with { Capacity = 1 })
            .WithPeriod(new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 20))
            .BuildConfirmed();

        // Act
        var result = ListingOccupancy.Of([leaving]).EnsureFits(BookingBuilder.Period(), 1, capacity: 1);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void PetsOn_Should_SumEveryStayCoveringTheNight()
    {
        // Arrange
        var first = new BookingBuilder().BuildConfirmed();
        var second = new BookingBuilder().WithPets(BookingBuilder.Thor()).BuildInProgress();

        // Act
        var pets = ListingOccupancy.Of([first, second]).PetsOn(new DateOnly(2026, 10, 21));

        // Assert
        pets.Should().Be(2);
    }
}
