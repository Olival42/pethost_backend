using FluentAssertions;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Bookings.Domain.UnitTests.Bookings;

/// <summary>
/// Agenda do anfitrião no pedido, cancelamento pela plataforma quando o admin suspende um dos
/// lados e o reembolso do cancelamento feito pelo admin.
/// </summary>
public sealed class BookingPlatformTests
{
    private static readonly DateTimeOffset Now = BookingBuilder.DefaultNow;

    // ----- agenda -----

    [Theory]
    [InlineData(2026, 10, 22, 2026, 10, 23)]
    [InlineData(2026, 10, 25, 2026, 10, 28)] // o dia da saída também conta
    [InlineData(2026, 10, 15, 2026, 10, 20)] // o dia da chegada também conta
    public void Request_Should_FailWithDatesBlocked_When_AnyDayOfTheStayIsClosed(int fromYear, int fromMonth, int fromDay, int toYear, int toMonth, int toDay)
    {
        // Arrange — estadia de 20 a 25/10.
        var closed = new ClosedPeriod(new DateOnly(fromYear, fromMonth, fromDay), new DateOnly(toYear, toMonth, toDay));
        var listing = BookingBuilder.Listing() with { ClosedPeriods = [closed] };

        // Act
        var result = Booking.Request(
            BookingBuilder.OwnerId,
            listing,
            BookingBuilder.Period(),
            [BookingBuilder.PipocaId],
            [BookingBuilder.Pipoca()],
            [],
            ListingOccupancy.Empty,
            Now);

        // Assert
        var firstBlocked = closed.StartDate > BookingBuilder.DefaultCheckIn ? closed.StartDate : BookingBuilder.DefaultCheckIn;
        result.FirstError.Should().Be(BookingsErrors.DatesBlocked(firstBlocked));
    }

    [Fact]
    public void Request_Should_Succeed_When_TheClosedPeriodDoesNotTouchTheStay()
    {
        // Arrange — fechado de 26 a 30/10; estadia de 20 a 25/10.
        var listing = BookingBuilder.Listing() with { ClosedPeriods = [new ClosedPeriod(new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 30))] };

        // Act
        var result = Booking.Request(
            BookingBuilder.OwnerId,
            listing,
            BookingBuilder.Period(),
            [BookingBuilder.PipocaId],
            [BookingBuilder.Pipoca()],
            [],
            ListingOccupancy.Empty,
            Now);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void FirstBlockedDay_Should_BeNull_When_ThereIsNoClosedPeriod()
    {
        // Act
        var day = BookingBuilder.Listing().FirstBlockedDay(BookingBuilder.Period());

        // Assert
        day.Should().BeNull();
    }

    // ----- suspensão -----

    [Fact]
    public void CancelForSuspension_Should_RefundTheOwnerInFull_When_TheHostIsSuspendedAfterPayment()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();
        var at = Now.AddDays(1);

        // Act
        var cancelled = booking.CancelForSuspension(BookingParty.Host, at);

        // Assert
        cancelled.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Cancelled);
        booking.CancelledBy.Should().Be(BookingParty.Platform);
        booking.RefundPercentage.Should().Be(Booking.FullRefundPercentage);
        booking.CancellationReason.Should().Be(Booking.HostSuspendedReason);
        booking.CancelledAt.Should().Be(at);
    }

    [Fact]
    public void CancelForSuspension_Should_NotRefund_When_TheOwnerIsSuspendedAfterPayment()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();

        // Act
        var cancelled = booking.CancelForSuspension(BookingParty.Owner, Now.AddDays(1));

        // Assert — o que o tutor pagou fica com o anfitrião (90/10).
        cancelled.Should().BeTrue();
        booking.RefundPercentage.Should().Be(Booking.NoRefundPercentage);
        booking.CancellationReason.Should().Be(Booking.OwnerSuspendedReason);
    }

    [Theory]
    [InlineData(BookingParty.Owner)]
    [InlineData(BookingParty.Host)]
    public void CancelForSuspension_Should_CancelWithoutRefund_When_NothingWasPaid(BookingParty suspended)
    {
        // Arrange
        var requested = new BookingBuilder().Build();
        var accepted = new BookingBuilder().BuildAccepted();

        // Act
        var first = requested.CancelForSuspension(suspended, Now.AddHours(5));
        var second = accepted.CancelForSuspension(suspended, Now.AddHours(5));

        // Assert
        first.Should().BeTrue();
        second.Should().BeTrue();
        requested.RefundPercentage.Should().BeNull();
        accepted.RefundPercentage.Should().BeNull();
        accepted.OccupiesCapacity.Should().BeFalse();
    }

    [Fact]
    public void CancelForSuspension_Should_KeepTheStay_When_ThePetAlreadyArrived()
    {
        // Arrange
        var booking = new BookingBuilder().BuildInProgress();

        // Act
        var cancelled = booking.CancelForSuspension(BookingParty.Host, Now.AddDays(10));

        // Assert — o pet está com o anfitrião: o check-out (do admin) encerra.
        cancelled.Should().BeFalse();
        booking.Status.Should().Be(BookingStatus.InProgress);
    }

    [Fact]
    public void CancelForSuspension_Should_DoNothing_When_AlreadyCancelled()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();
        booking.Cancel(BookingParty.Owner, null, Now.AddHours(3));

        // Act
        var cancelled = booking.CancelForSuspension(BookingParty.Host, Now.AddHours(4));

        // Assert
        cancelled.Should().BeFalse();
        booking.CancelledBy.Should().Be(BookingParty.Owner);
    }

    [Fact]
    public void CancelForSuspension_Should_Throw_When_ThePlatformIsTheSuspendedSide()
    {
        // Arrange
        var booking = new BookingBuilder().Build();

        // Act
        var act = () => booking.CancelForSuspension(BookingParty.Platform, Now);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ----- admin -----

    [Fact]
    public void Cancel_Should_RefundInFull_When_ThePlatformCancelsOnTheCheckInDay()
    {
        // Arrange — no dia da chegada, o tutor levaria 50%; o admin devolve tudo.
        var booking = new BookingBuilder().BuildConfirmed();
        var checkInDay = BookingClock.StartOf(BookingBuilder.DefaultCheckIn).AddHours(8);

        // Act
        var result = booking.Cancel(BookingParty.Platform, "Anfitrião sem repasses no Stripe", checkInDay);

        // Assert
        result.IsSuccess.Should().BeTrue();
        booking.RefundPercentage.Should().Be(Booking.FullRefundPercentage);
        booking.CancelledBy.Should().Be(BookingParty.Platform);
        booking.CancellationReason.Should().Be("Anfitrião sem repasses no Stripe");
        booking.RefundFor(BookingParty.Owner, checkInDay).Should().Be(Booking.LateCancellationRefundPercentage);
    }
}
