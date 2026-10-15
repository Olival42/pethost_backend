using FluentAssertions;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Bookings.Domain.UnitTests.Bookings;

public sealed class BookingRequestTests
{
    private static readonly DateTimeOffset Now = BookingBuilder.DefaultNow;

    private static Shared.Kernel.Results.Result<Booking> Request(
        BookableListing? listing = null,
        IReadOnlyCollection<Guid>? petIds = null,
        IReadOnlyCollection<BookablePet>? pets = null,
        IReadOnlyCollection<Guid>? busy = null,
        StayPeriod? period = null,
        ListingOccupancy? occupancy = null,
        DateTimeOffset? now = null) =>
        Booking.Request(
            BookingBuilder.OwnerId,
            listing ?? BookingBuilder.Listing(),
            period ?? BookingBuilder.Period(),
            petIds ?? [BookingBuilder.PipocaId],
            pets ?? [BookingBuilder.Pipoca()],
            busy ?? [],
            occupancy ?? ListingOccupancy.Empty,
            now ?? Now);

    [Fact]
    public void Request_Should_ReturnCheckInTooSoon_When_CheckInIsLessThan72HoursAway()
    {
        // Act: 71h59 antes da meia-noite do dia 20.
        var result = Request(now: BookingClock.StartOf(BookingBuilder.DefaultCheckIn).AddHours(-72).AddMinutes(1));

        // Assert
        result.FirstError.Should().Be(BookingsErrors.CheckInTooSoon);
    }

    [Fact]
    public void Request_Should_LetTheHostAnswerUntil48HoursBefore_When_CheckInIsExactly72HoursAway()
    {
        // Arrange
        var now = BookingClock.StartOf(BookingBuilder.DefaultCheckIn).AddHours(-72);

        // Act
        var booking = Request(now: now).Value!;

        // Assert
        booking.ResponseDeadline.Should().Be(Booking.LatestAcceptance(BookingBuilder.DefaultCheckIn));
        booking.ResponseDeadline.Should().Be(now.AddHours(24));
    }

    [Fact]
    public void LatestAcceptance_Should_Be48HoursBeforeMidnightOfTheCheckInDay()
    {
        // Act
        var latest = Booking.LatestAcceptance(new DateOnly(2026, 10, 20));

        // Assert: 18/10 00:00 em Brasília.
        latest.Should().Be(new DateTimeOffset(2026, 10, 18, 0, 0, 0, BookingClock.LocalOffset));
    }

    [Theory]
    [InlineData(0, 0, 19)] // 18/10 00:00: chegada dia 20 está a exatas 48h, ainda aceitável.
    [InlineData(0, 1, 20)] // 18/10 00:01: chegada dia 20 já não dá.
    [InlineData(23, 59, 20)]
    public void LastCheckInClosedToAcceptance_Should_FollowThe48HourCutoff(int hour, int minute, int expectedDay)
    {
        // Arrange
        var now = new DateTimeOffset(2026, 10, 18, hour, minute, 0, BookingClock.LocalOffset);

        // Act
        var closed = Booking.LastCheckInClosedToAcceptance(now);

        // Assert
        closed.Should().Be(new DateOnly(2026, 10, expectedDay));
    }

    [Fact]
    public void Request_Should_CreateRequestedBookingWithTotalPrice_When_EverythingIsValid()
    {
        // Act: 2 pets × 5 noites × R$ 60.
        var result = Request(
            petIds: [BookingBuilder.PipocaId, BookingBuilder.ThorId],
            pets: [BookingBuilder.Pipoca(), BookingBuilder.Thor()]);

        // Assert
        var booking = result.Value!;
        booking.Status.Should().Be(BookingStatus.Requested);
        booking.OwnerId.Should().Be(BookingBuilder.OwnerId);
        booking.ListingId.Should().Be(BookingBuilder.ListingId);
        booking.HostId.Should().Be(BookingBuilder.HostId);
        booking.PetCount.Should().Be(2);
        booking.Pets.Select(p => p.PetId).Should().Equal(BookingBuilder.PipocaId, BookingBuilder.ThorId);
        booking.NightlyRate.Should().Be(60m);
        booking.TotalPrice.Should().Be(600m);
        booking.RequestedAt.Should().Be(Now);
        booking.ResponseDeadline.Should().Be(Now.AddHours(24));
    }

    [Fact]
    public void Request_Should_ReturnListingNotFound_When_ListingIsNotBookable()
    {
        // Act
        var result = Request(listing: BookingBuilder.Listing() with { IsBookable = false });

        // Assert
        result.FirstError.Should().Be(BookingsErrors.ListingNotFound(BookingBuilder.ListingId));
    }

    [Fact]
    public void Request_Should_ReturnPetNotFound_When_PetDoesNotExist()
    {
        // Act
        var result = Request(pets: []);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.PetNotFound(BookingBuilder.PipocaId));
    }

    [Fact]
    public void Request_Should_ReturnPetNotFound_When_PetBelongsToAnotherOwner()
    {
        // Act
        var result = Request(pets: [BookingBuilder.Pipoca() with { OwnerId = Guid.CreateVersion7() }]);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.PetNotFound(BookingBuilder.PipocaId));
    }

    [Fact]
    public void Request_Should_ReturnPetNotFound_When_PetBelongsToAHost()
    {
        // Act
        var result = Request(pets: [BookingBuilder.Pipoca() with { OwnerId = null }]);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.PetNotFound(BookingBuilder.PipocaId));
    }

    [Fact]
    public void Request_Should_ReturnPetInactive_When_PetIsDeactivated()
    {
        // Act
        var result = Request(pets: [BookingBuilder.Pipoca() with { IsActive = false }]);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.PetInactive("Pipoca"));
    }

    [Fact]
    public void Request_Should_ReturnSpeciesNotAccepted_When_ListingDoesNotTakeTheSpecies()
    {
        // Act
        var result = Request(listing: BookingBuilder.Listing() with { AcceptedSpecies = ["cat"] });

        // Assert
        result.FirstError.Should().Be(BookingsErrors.SpeciesNotAccepted("Pipoca", "dog"));
    }

    [Fact]
    public void Request_Should_ReturnSizeNotAccepted_When_ListingDoesNotTakeTheSize()
    {
        // Act
        var result = Request(pets: [BookingBuilder.Pipoca() with { Size = "large" }]);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.SizeNotAccepted("Pipoca", "large"));
    }

    [Fact]
    public void Request_Should_ReturnPetPeriodConflict_When_PetHasAnotherBookingInThePeriod()
    {
        // Act
        var result = Request(busy: [BookingBuilder.PipocaId]);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.PetPeriodConflict("Pipoca"));
    }

    [Fact]
    public void Request_Should_ReturnWeekdayNotAvailable_When_ListingDoesNotReceiveOnADayOfTheStay()
    {
        // Arrange: a estadia (ter 20 a dom 25) passa pelo sábado, e o cantinho não recebe no sábado.
        var listing = BookingBuilder.Listing() with
        {
            AvailableWeekdays = [.. Enum.GetValues<DayOfWeek>().Where(d => d != DayOfWeek.Saturday)],
        };

        // Act
        var result = Request(listing: listing);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.WeekdayNotAvailable(new DateOnly(2026, 10, 24)));
    }

    [Fact]
    public void Request_Should_ReturnCapacityExceeded_When_ListingIsFull()
    {
        // Arrange
        var full = new BookingBuilder()
            .WithListing(l => l with { Capacity = 1 })
            .BuildConfirmed();

        // Act
        var result = Request(
            listing: BookingBuilder.Listing() with { Capacity = 1 },
            occupancy: ListingOccupancy.Of([full]));

        // Assert
        result.FirstError.Should().Be(BookingsErrors.CapacityExceeded(BookingBuilder.DefaultCheckIn));
    }
}

public sealed class BookingTransitionsTests
{
    private static readonly DateTimeOffset Now = BookingBuilder.DefaultNow;

    /// <summary>Meia-noite do dia de chegada (20/10), horário de Brasília.</summary>
    private static readonly DateTimeOffset CheckInStart = BookingClock.StartOf(BookingBuilder.DefaultCheckIn);

    // ----- aceitar -----

    [Fact]
    public void Accept_Should_AwaitPaymentAndHoldCapacity_When_RequestedWithinTheDeadline()
    {
        // Arrange
        var booking = new BookingBuilder().Build();
        var at = Now.AddHours(2);

        // Act
        var result = booking.Accept(BookingBuilder.Listing(), ListingOccupancy.Empty, at);

        // Assert
        result.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Accepted);
        booking.RespondedAt.Should().Be(at);
        booking.ConfirmedAt.Should().BeNull();
        booking.PaymentDeadline.Should().Be(at + Booking.PaymentWindow);
        booking.OccupiesCapacity.Should().BeTrue();
        booking.RevealsContact.Should().BeFalse();
    }

    [Fact]
    public void Accept_Should_ReturnNotAwaitingResponse_When_AlreadyConfirmed()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();

        // Act
        var result = booking.Accept(BookingBuilder.Listing(), ListingOccupancy.Empty, Now.AddHours(2));

        // Assert
        result.FirstError.Should().Be(BookingsErrors.NotAwaitingResponse);
    }

    [Fact]
    public void Accept_Should_ReturnResponseExpired_When_MoreThan24HoursPassed()
    {
        // Arrange
        var booking = new BookingBuilder().Build();

        // Act
        var result = booking.Accept(BookingBuilder.Listing(), ListingOccupancy.Empty, Now.AddHours(24).AddMinutes(1));

        // Assert
        result.FirstError.Should().Be(BookingsErrors.ResponseExpired);
        booking.Status.Should().Be(BookingStatus.Requested);
    }

    [Fact]
    public void Accept_Should_ReturnCapacityExceeded_When_AnotherStayWasConfirmedMeanwhile()
    {
        // Arrange
        var booking = new BookingBuilder().Build();
        var other = new BookingBuilder().WithPets(BookingBuilder.Thor()).BuildConfirmed();

        // Act
        var result = booking.Accept(BookingBuilder.Listing() with { Capacity = 1 }, ListingOccupancy.Of([other]), Now.AddHours(1));

        // Assert
        result.FirstError.Should().Be(BookingsErrors.CapacityExceeded(BookingBuilder.DefaultCheckIn));
        booking.Status.Should().Be(BookingStatus.Requested);
    }

    // ----- recusar e expirar -----

    [Fact]
    public void Decline_Should_CloseWithTrimmedReason_When_Requested()
    {
        // Arrange
        var booking = new BookingBuilder().Build();

        // Act
        var result = booking.Decline("  Vou viajar nessas datas.  ", Now.AddHours(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Declined);
        booking.DeclineReason.Should().Be("Vou viajar nessas datas.");
        booking.RespondedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void Decline_Should_ReturnNotAwaitingResponse_When_AlreadyConfirmed()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();

        // Act
        var result = booking.Decline(null, Now.AddHours(2));

        // Assert
        result.FirstError.Should().Be(BookingsErrors.NotAwaitingResponse);
    }

    [Fact]
    public void Decline_Should_ReturnResponseExpired_When_DeadlinePassed()
    {
        // Arrange
        var booking = new BookingBuilder().Build();

        // Act
        var result = booking.Decline(null, Now.AddDays(2));

        // Assert
        result.FirstError.Should().Be(BookingsErrors.ResponseExpired);
    }

    [Fact]
    public void Expire_Should_CloseTheRequest_When_DeadlinePassed()
    {
        // Arrange
        var booking = new BookingBuilder().Build();
        var at = Now.AddHours(25);

        // Act
        var expired = booking.Expire(at);

        // Assert
        expired.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Expired);
        booking.ExpiredAt.Should().Be(at);
    }

    [Fact]
    public void Expire_Should_DoNothing_When_StillWithinTheDeadline()
    {
        // Arrange
        var booking = new BookingBuilder().Build();

        // Act
        var expired = booking.Expire(Now.AddHours(23));

        // Assert
        expired.Should().BeFalse();
        booking.Status.Should().Be(BookingStatus.Requested);
    }

    [Fact]
    public void Expire_Should_DoNothing_When_AlreadyAnswered()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();

        // Act
        var expired = booking.Expire(Now.AddDays(3));

        // Assert
        expired.Should().BeFalse();
        booking.Status.Should().Be(BookingStatus.Confirmed);
    }

    // ----- cancelar -----

    [Theory]
    [InlineData(BookingParty.Owner)]
    [InlineData(BookingParty.Host)]
    public void Cancel_Should_CloseWithoutRefund_When_NothingWasPaidYet(BookingParty by)
    {
        // Arrange
        var booking = new BookingBuilder().Build();

        // Act
        var result = booking.Cancel(by, " Mudei de planos ", Now.AddHours(1));

        // Assert
        result.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Cancelled);
        booking.CancelledBy.Should().Be(by);
        booking.CancellationReason.Should().Be("Mudei de planos");
        booking.CancelledAt.Should().Be(Now.AddHours(1));
        booking.RefundPercentage.Should().BeNull();
    }

    [Fact]
    public void Cancel_Should_RefundEverything_When_OwnerCancelsExactly48HoursBeforeCheckIn()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();

        // Act
        booking.Cancel(BookingParty.Owner, null, CheckInStart.AddHours(-48));

        // Assert
        booking.RefundPercentage.Should().Be(Booking.FullRefundPercentage);
    }

    [Fact]
    public void Cancel_Should_RefundHalf_When_OwnerCancelsLessThan48HoursBeforeCheckIn()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();

        // Act
        booking.Cancel(BookingParty.Owner, null, CheckInStart.AddHours(-47));

        // Assert
        booking.RefundPercentage.Should().Be(Booking.LateCancellationRefundPercentage);
    }

    [Fact]
    public void Cancel_Should_RefundEverything_When_HostCancelsEvenOnCheckInDay()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();

        // Act
        booking.Cancel(BookingParty.Host, null, CheckInStart.AddHours(9));

        // Assert
        booking.RefundPercentage.Should().Be(Booking.FullRefundPercentage);
    }

    [Fact]
    public void Cancel_Should_ReturnAlreadyCancelled_When_CancelledTwice()
    {
        // Arrange
        var booking = new BookingBuilder().Build();
        booking.Cancel(BookingParty.Owner, null, Now.AddHours(1));

        // Act
        var result = booking.Cancel(BookingParty.Owner, null, Now.AddHours(2));

        // Assert
        result.FirstError.Should().Be(BookingsErrors.AlreadyCancelled);
    }

    [Fact]
    public void Cancel_Should_ReturnCannotCancelAfterCheckIn_When_PetAlreadyArrived()
    {
        // Arrange
        var booking = new BookingBuilder().BuildInProgress();

        // Act
        var result = booking.Cancel(BookingParty.Owner, null, CheckInStart.AddHours(12));

        // Assert
        result.FirstError.Should().Be(BookingsErrors.CannotCancelAfterCheckIn);
        booking.Status.Should().Be(BookingStatus.InProgress);
    }

    [Fact]
    public void Cancel_Should_ReturnCannotCancelAfterCheckIn_When_StayIsCompleted()
    {
        // Arrange
        var booking = new BookingBuilder().BuildInProgress();
        booking.CheckOut(CheckInStart.AddDays(5));

        // Act
        var result = booking.Cancel(BookingParty.Host, null, CheckInStart.AddDays(6));

        // Assert
        result.FirstError.Should().Be(BookingsErrors.CannotCancelAfterCheckIn);
    }

    [Fact]
    public void Cancel_Should_ReturnAlreadyClosed_When_BookingWasDeclined()
    {
        // Arrange
        var booking = new BookingBuilder().Build();
        booking.Decline(null, Now.AddHours(1));

        // Act
        var result = booking.Cancel(BookingParty.Owner, null, Now.AddHours(2));

        // Assert
        result.FirstError.Should().Be(BookingsErrors.AlreadyClosed);
    }

    // ----- check-in -----

    [Fact]
    public void CheckIn_Should_StartTheStay_When_ConfirmedOnCheckInDay()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();
        var at = CheckInStart.AddHours(14);

        // Act
        var result = booking.CheckIn(at);

        // Assert
        result.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.InProgress);
        booking.CheckedInAt.Should().Be(at);
        booking.OccupiesCapacity.Should().BeTrue();
    }

    [Fact]
    public void CheckIn_Should_ReturnNotConfirmed_When_StillRequested()
    {
        // Arrange
        var booking = new BookingBuilder().Build();

        // Act
        var result = booking.CheckIn(CheckInStart.AddHours(14));

        // Assert
        result.FirstError.Should().Be(BookingsErrors.NotConfirmed);
    }

    [Fact]
    public void CheckIn_Should_ReturnNotConfirmed_When_AlreadyCheckedIn()
    {
        // Arrange
        var booking = new BookingBuilder().BuildInProgress();

        // Act
        var result = booking.CheckIn(CheckInStart.AddHours(15));

        // Assert
        result.FirstError.Should().Be(BookingsErrors.NotConfirmed);
    }

    [Fact]
    public void CheckIn_Should_ReturnTooEarly_When_BeforeCheckInDayInBrasilia()
    {
        // Arrange: 20/10 às 02:00 UTC ainda é 19/10 às 23:00 em Brasília.
        var booking = new BookingBuilder().BuildConfirmed();

        // Act
        var result = booking.CheckIn(new DateTimeOffset(2026, 10, 20, 2, 0, 0, TimeSpan.Zero));

        // Assert
        result.FirstError.Should().Be(BookingsErrors.CheckInTooEarly(BookingBuilder.DefaultCheckIn));
    }

    [Fact]
    public void CheckIn_Should_ReturnWindowClosed_When_CheckOutDayArrived()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();

        // Act
        var result = booking.CheckIn(BookingClock.StartOf(BookingBuilder.DefaultCheckOut).AddHours(8));

        // Assert
        result.FirstError.Should().Be(BookingsErrors.CheckInWindowClosed);
    }

    // ----- check-out -----

    [Fact]
    public void CheckOut_Should_CompleteTheStay_When_InProgress()
    {
        // Arrange
        var booking = new BookingBuilder().BuildInProgress();
        var at = BookingClock.StartOf(BookingBuilder.DefaultCheckOut).AddHours(10);

        // Act
        var result = booking.CheckOut(at);

        // Assert
        result.IsSuccess.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Completed);
        booking.CheckedOutAt.Should().Be(at);
        booking.OccupiesCapacity.Should().BeFalse();
    }

    [Fact]
    public void CheckOut_Should_AllowEarlyPickUp_When_BeforeCheckOutDay()
    {
        // Arrange
        var booking = new BookingBuilder().BuildInProgress();

        // Act
        var result = booking.CheckOut(CheckInStart.AddDays(2));

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void CheckOut_Should_ReturnNotInProgress_When_PetHasNotArrived()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();

        // Act
        var result = booking.CheckOut(CheckInStart.AddDays(5));

        // Assert
        result.FirstError.Should().Be(BookingsErrors.NotInProgress);
        booking.Status.Should().Be(BookingStatus.Confirmed);
    }
}

public sealed class BookingValuesTests
{
    [Theory]
    [InlineData(BookingStatus.Requested, "requested")]
    [InlineData(BookingStatus.InProgress, "in_progress")]
    [InlineData(BookingStatus.Cancelled, "cancelled")]
    public void BookingStatusValues_Should_RoundTrip(BookingStatus status, string wire)
    {
        // Act
        var parsed = BookingStatusValues.TryParse(wire.ToUpperInvariant(), out var back);

        // Assert
        BookingStatusValues.ToWire(status).Should().Be(wire);
        parsed.Should().BeTrue();
        back.Should().Be(status);
    }

    [Fact]
    public void BookingStatusValues_Should_RejectUnknownValue()
    {
        // Act
        var parsed = BookingStatusValues.TryParse("paid", out _);

        // Assert
        parsed.Should().BeFalse();
    }

    [Theory]
    [InlineData(BookingParty.Owner, "owner")]
    [InlineData(BookingParty.Host, "host")]
    public void BookingPartyValues_Should_RoundTrip(BookingParty party, string wire)
    {
        // Assert
        BookingPartyValues.ToWire(party).Should().Be(wire);
        BookingPartyValues.FromWire(wire).Should().Be(party);
    }

    [Fact]
    public void BookingClock_Today_Should_UseBrasiliaTime()
    {
        // Act: 02:00 UTC de 20/10 = 23:00 de 19/10 em Brasília.
        var today = BookingClock.Today(new DateTimeOffset(2026, 10, 20, 2, 0, 0, TimeSpan.Zero));

        // Assert
        today.Should().Be(new DateOnly(2026, 10, 19));
    }
}
