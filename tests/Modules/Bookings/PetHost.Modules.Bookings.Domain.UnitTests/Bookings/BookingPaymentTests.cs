using FluentAssertions;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Bookings.Domain.UnitTests.Bookings;

/// <summary>O que o pagamento muda na reserva: prazo para pagar, confirmação, expiração do aceite e cancelamento pela plataforma.</summary>
public sealed class BookingPaymentTests
{
    private static readonly DateTimeOffset Now = BookingBuilder.DefaultNow;

    [Fact]
    public void PaymentDeadline_Should_Be24HoursAfterAcceptance_When_CheckInIsFarAway()
    {
        // Arrange
        var booking = new BookingBuilder().BuildAccepted();

        // Act
        var deadline = booking.PaymentDeadline;

        // Assert
        deadline.Should().Be(booking.RespondedAt!.Value + Booking.PaymentWindow);
    }

    [Fact]
    public void PaymentDeadline_Should_BeNull_When_NotAccepted()
    {
        // Arrange
        var requested = new BookingBuilder().Build();
        var confirmed = new BookingBuilder().BuildConfirmed();

        // Assert
        requested.PaymentDeadline.Should().BeNull();
        confirmed.PaymentDeadline.Should().BeNull();
    }

    [Fact]
    public void ConfirmPayment_Should_ConfirmAndRevealContact_When_Accepted()
    {
        // Arrange
        var booking = new BookingBuilder().BuildAccepted();
        var at = Now.AddHours(3);

        // Act
        var result = booking.ConfirmPayment(at);

        // Assert
        result.Value.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Confirmed);
        booking.ConfirmedAt.Should().Be(at);
        booking.RevealsContact.Should().BeTrue();
        booking.OccupiesCapacity.Should().BeTrue();
    }

    [Fact]
    public void ConfirmPayment_Should_ConfirmEvenAfterTheDeadline_When_StillAccepted()
    {
        // Arrange — o Stripe confirmou depois do prazo, antes de a expiração rodar.
        var booking = new BookingBuilder().BuildAccepted();

        // Act
        var result = booking.ConfirmPayment(booking.PaymentDeadline!.Value.AddMinutes(5));

        // Assert
        result.Value.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Confirmed);
    }

    [Fact]
    public void ConfirmPayment_Should_ReturnFalseWithoutChanges_When_AlreadyConfirmed()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();
        var confirmedAt = booking.ConfirmedAt;

        // Act
        var result = booking.ConfirmPayment(Now.AddHours(5));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
        booking.ConfirmedAt.Should().Be(confirmedAt);
    }

    [Fact]
    public void ConfirmPayment_Should_ReturnFalse_When_AlreadyCheckedIn()
    {
        // Arrange
        var booking = new BookingBuilder().BuildInProgress();

        // Act
        var result = booking.ConfirmPayment(Now.AddDays(10));

        // Assert
        result.Value.Should().BeFalse();
        booking.Status.Should().Be(BookingStatus.InProgress);
    }

    [Fact]
    public void ConfirmPayment_Should_ReturnNotAwaitingPayment_When_StillRequested()
    {
        // Arrange
        var booking = new BookingBuilder().Build();

        // Act
        var result = booking.ConfirmPayment(Now.AddHours(1));

        // Assert
        result.FirstError.Should().Be(BookingsErrors.NotAwaitingPayment);
        booking.Status.Should().Be(BookingStatus.Requested);
    }

    [Fact]
    public void ConfirmPayment_Should_ReturnNotAwaitingPayment_When_CancelledWhileTheOwnerWasPaying()
    {
        // Arrange
        var booking = new BookingBuilder().BuildAccepted();
        booking.Cancel(BookingParty.Host, null, Now.AddHours(2));

        // Act
        var result = booking.ConfirmPayment(Now.AddHours(3));

        // Assert
        result.FirstError.Should().Be(BookingsErrors.NotAwaitingPayment);
        booking.Status.Should().Be(BookingStatus.Cancelled);
    }

    [Fact]
    public void Expire_Should_ExpireAcceptedBooking_When_PaymentWindowIsOver()
    {
        // Arrange
        var booking = new BookingBuilder().BuildAccepted();
        var at = booking.PaymentDeadline!.Value.AddSeconds(1);

        // Act
        var expired = booking.Expire(at);

        // Assert
        expired.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Expired);
        booking.ExpiredAt.Should().Be(at);
        booking.OccupiesCapacity.Should().BeFalse();
    }

    [Fact]
    public void Expire_Should_KeepAcceptedBooking_When_StillWithinThePaymentWindow()
    {
        // Arrange
        var booking = new BookingBuilder().BuildAccepted();

        // Act
        var expired = booking.Expire(booking.PaymentDeadline!.Value);

        // Assert
        expired.Should().BeFalse();
        booking.Status.Should().Be(BookingStatus.Accepted);
    }

    [Fact]
    public void Cancel_Should_HaveNoRefund_When_AcceptedButNotPaid()
    {
        // Arrange
        var booking = new BookingBuilder().BuildAccepted();

        // Act
        var result = booking.Cancel(BookingParty.Owner, null, Now.AddHours(2));

        // Assert
        result.IsSuccess.Should().BeTrue();
        booking.RefundPercentage.Should().BeNull();
    }

    [Fact]
    public void CancelUnpaid_Should_CancelByThePlatformWithFullRefund_When_Confirmed()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();
        var at = BookingClock.StartOf(BookingBuilder.DefaultCheckIn).AddHours(-10);

        // Act
        var result = booking.CancelUnpaid(at);

        // Assert
        result.Value.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Cancelled);
        booking.CancelledBy.Should().Be(BookingParty.Platform);
        booking.RefundPercentage.Should().Be(Booking.FullRefundPercentage);
        booking.CancelledAt.Should().Be(at);
    }

    [Fact]
    public void CancelUnpaid_Should_ReturnFalse_When_AlreadyCancelled()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();
        booking.Cancel(BookingParty.Owner, "Mudei de planos", Now.AddHours(3));

        // Act
        var result = booking.CancelUnpaid(Now.AddHours(4));

        // Assert
        result.Value.Should().BeFalse();
        booking.CancelledBy.Should().Be(BookingParty.Owner);
    }

    [Fact]
    public void CancelUnpaid_Should_ReturnCannotCancelAfterCheckIn_When_PetAlreadyArrived()
    {
        // Arrange
        var booking = new BookingBuilder().BuildInProgress();

        // Act
        var result = booking.CancelUnpaid(Now.AddDays(10));

        // Assert
        result.FirstError.Should().Be(BookingsErrors.CannotCancelAfterCheckIn);
        booking.Status.Should().Be(BookingStatus.InProgress);
    }

    [Fact]
    public void CancelUnpaid_Should_ReturnAlreadyClosed_When_NotConfirmed()
    {
        // Arrange
        var booking = new BookingBuilder().BuildAccepted();

        // Act
        var result = booking.CancelUnpaid(Now.AddHours(2));

        // Assert
        result.FirstError.Should().Be(BookingsErrors.AlreadyClosed);
        booking.Status.Should().Be(BookingStatus.Accepted);
    }

    [Theory]
    [InlineData(BookingParty.Owner, "owner")]
    [InlineData(BookingParty.Host, "host")]
    [InlineData(BookingParty.Platform, "platform")]
    public void BookingPartyValues_Should_RoundTrip_When_AnyParty(BookingParty party, string wire)
    {
        // Act
        var written = BookingPartyValues.ToWire(party);

        // Assert
        written.Should().Be(wire);
        BookingPartyValues.FromWire(wire).Should().Be(party);
    }

    [Fact]
    public void BookingPartyValues_Should_Throw_When_ValueIsUnknown()
    {
        // Act
        var act = () => BookingPartyValues.FromWire("admin");

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
