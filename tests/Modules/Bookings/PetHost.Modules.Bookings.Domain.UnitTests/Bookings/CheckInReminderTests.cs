using FluentAssertions;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Bookings.Domain.UnitTests.Bookings;

/// <summary>Lembrete da véspera do check-in.</summary>
public sealed class CheckInReminderTests
{
    /// <summary>10h (Brasília) de um dia em relação ao dia de chegada padrão (20/10).</summary>
    private static DateTimeOffset DaysBeforeCheckIn(int days) =>
        BookingClock.StartOf(BookingBuilder.DefaultCheckIn.AddDays(-days)).AddHours(10);

    [Fact]
    public void TakeCheckInReminder_Should_RemindOnce_When_ItIsTheEveOfAConfirmedStay()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();

        // Act
        var first = booking.TakeCheckInReminder(DaysBeforeCheckIn(1));
        var again = booking.TakeCheckInReminder(DaysBeforeCheckIn(1).AddHours(5));

        // Assert
        first.Should().BeTrue();
        again.Should().BeFalse("o lembrete vai uma vez só");
        booking.CheckInRemindedAt.Should().Be(DaysBeforeCheckIn(1));
    }

    [Theory]
    [InlineData(2)] // cedo demais
    [InlineData(0)] // já é o dia da chegada
    public void TakeCheckInReminder_Should_NotRemind_When_ItIsNotTheEve(int daysBefore)
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();

        // Act
        var reminded = booking.TakeCheckInReminder(DaysBeforeCheckIn(daysBefore));

        // Assert
        reminded.Should().BeFalse();
        booking.CheckInRemindedAt.Should().BeNull();
    }

    [Fact]
    public void TakeCheckInReminder_Should_NotRemind_When_TheBookingIsNotPaid()
    {
        // Arrange
        var accepted = new BookingBuilder().BuildAccepted();

        // Act
        var reminded = accepted.TakeCheckInReminder(DaysBeforeCheckIn(1));

        // Assert
        reminded.Should().BeFalse();
    }
}
