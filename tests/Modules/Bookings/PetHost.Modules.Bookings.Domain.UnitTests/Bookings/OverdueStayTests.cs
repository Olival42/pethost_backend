using FluentAssertions;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Bookings.Domain.UnitTests.Bookings;

/// <summary>Estadias que já deviam ter acabado: pet que não chegou, lembrete e check-out automático.</summary>
public sealed class OverdueStayTests
{
    /// <summary>10h (Brasília) de um dia em relação ao dia de saída padrão (25/10).</summary>
    private static DateTimeOffset DaysAfterCheckOut(int days) =>
        BookingClock.StartOf(BookingBuilder.DefaultCheckOut.AddDays(days)).AddHours(10);

    [Fact]
    public void CloseOverdueStay_Should_CancelWithHalfRefund_When_ThePaidStayNeverStarted()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();
        var at = DaysAfterCheckOut(0);

        // Act
        var closing = booking.CloseOverdueStay(at);

        // Assert
        closing.Should().Be(StayClosing.NoShow);
        booking.Status.Should().Be(BookingStatus.Cancelled);
        booking.CancelledBy.Should().Be(BookingParty.Platform);
        booking.RefundPercentage.Should().Be(Booking.NoShowRefundPercentage).And.Be(50);
        booking.CancellationReason.Should().Be(Booking.NoShowReason);
        booking.CancelledAt.Should().Be(at);
    }

    [Fact]
    public void CloseOverdueStay_Should_ChangeNothing_When_ItIsBeforeTheCheckOutDay()
    {
        // Arrange — último dia em que ainda dava para fazer o check-in.
        var confirmed = new BookingBuilder().BuildConfirmed();
        var inProgress = new BookingBuilder().BuildInProgress();

        // Act
        var first = confirmed.CloseOverdueStay(DaysAfterCheckOut(-1));
        var second = inProgress.CloseOverdueStay(DaysAfterCheckOut(-1));

        // Assert
        first.Should().Be(StayClosing.None);
        second.Should().Be(StayClosing.None);
        confirmed.Status.Should().Be(BookingStatus.Confirmed);
        inProgress.CheckOutRemindedAt.Should().BeNull();
    }

    [Fact]
    public void CloseOverdueStay_Should_RemindOnceAndThenCheckOut_When_TheHostForgetsTheCheckOut()
    {
        // Arrange
        var booking = new BookingBuilder().BuildInProgress();

        // Act
        var onCheckOutDay = booking.CloseOverdueStay(DaysAfterCheckOut(0));
        var nextDay = booking.CloseOverdueStay(DaysAfterCheckOut(1));
        var twoDaysLater = booking.CloseOverdueStay(DaysAfterCheckOut(Booking.AutoCheckOutAfterDays - 1));
        var afterTheGrace = booking.CloseOverdueStay(DaysAfterCheckOut(Booking.AutoCheckOutAfterDays));

        // Assert
        onCheckOutDay.Should().Be(StayClosing.CheckOutReminder);
        nextDay.Should().Be(StayClosing.None, "o lembrete vai uma vez só");
        twoDaysLater.Should().Be(StayClosing.None);
        afterTheGrace.Should().Be(StayClosing.AutoCheckedOut);
        booking.Status.Should().Be(BookingStatus.Completed);
        booking.CheckOutRemindedAt.Should().Be(DaysAfterCheckOut(0));
        booking.CheckedOutAt.Should().Be(DaysAfterCheckOut(Booking.AutoCheckOutAfterDays));
    }

    [Fact]
    public void CloseOverdueStay_Should_CheckOutWithoutTheReminder_When_TheRoutineOnlyRunsAfterTheGrace()
    {
        // Arrange — a tarefa ficou parada: o lembrete nunca foi.
        var booking = new BookingBuilder().BuildInProgress();

        // Act
        var closing = booking.CloseOverdueStay(DaysAfterCheckOut(10));

        // Assert
        closing.Should().Be(StayClosing.AutoCheckedOut);
        booking.CheckOutRemindedAt.Should().BeNull();
    }

    [Fact]
    public void CloseOverdueStay_Should_ChangeNothing_When_TheBookingIsAlreadyClosed()
    {
        // Arrange
        var completed = new BookingBuilder().BuildInProgress();
        completed.CheckOut(DaysAfterCheckOut(-1));
        var cancelled = new BookingBuilder().BuildConfirmed();
        cancelled.Cancel(BookingParty.Owner, null, BookingBuilder.DefaultNow.AddDays(1));
        var requested = new BookingBuilder().Build();

        // Act + Assert
        completed.CloseOverdueStay(DaysAfterCheckOut(5)).Should().Be(StayClosing.None);
        cancelled.CloseOverdueStay(DaysAfterCheckOut(5)).Should().Be(StayClosing.None);
        requested.CloseOverdueStay(DaysAfterCheckOut(5)).Should().Be(StayClosing.None);
        cancelled.RefundPercentage.Should().Be(Booking.FullRefundPercentage);
    }
}
