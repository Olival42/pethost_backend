using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.AcceptBooking;
using PetHost.Modules.Bookings.Application.Bookings.DeclineBooking;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Bookings.Application.UnitTests.Bookings;

public sealed class AcceptBookingCommandHandlerTests
{
    private readonly Booking _booking = new BookingBuilder().Build();
    private readonly Mock<IBookingRepository> _repository;
    private readonly Mock<IBookingsUnitOfWork> _unitOfWork = BookingsTestData.UnitOfWork();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Mock<IBookingNotifier> _notifier = new();
    private readonly Mock<IBookingParties> _parties = BookingsTestData.Parties();
    private Mock<IBookingListings> _listings = BookingsTestData.Listings();
    private DateTimeOffset _now = BookingBuilder.DefaultNow.AddHours(2);

    public AcceptBookingCommandHandlerTests() => _repository = BookingsTestData.Repository(_booking);

    private AcceptBookingCommandHandler Sut() => new(
        _repository.Object,
        _unitOfWork.Object,
        _parties.Object,
        _listings.Object,
        BookingsTestData.ResponseBuilder(),
        BookingsTestData.Notifications(_notifier.Object),
        _auditTrail.Object,
        new FakeTimeProvider(_now));

    private Task<Result<BookingResponse>> AcceptAsync(Guid? userId = null) =>
        Sut().HandleAsync(new AcceptBookingCommand(userId ?? BookingsTestData.HostUserId, _booking.Id.Value), CancellationToken.None);

    [Fact]
    public async Task HandleAsync_Should_AcceptUnderTheListingLockAndRecord_When_HostAccepts()
    {
        // Act
        var result = await AcceptAsync();

        // Assert
        result.Value!.Status.Should().Be("accepted");
        result.Value.ConfirmedAt.Should().BeNull();
        result.Value.PaymentDeadline.Should().Be(_now + Booking.PaymentWindow);
        result.Value.ResponseDeadline.Should().BeNull();
        _unitOfWork.Verify(u => u.RunLockedOnListingAsync(BookingBuilder.ListingId, It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.BookingAccepted), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_EmailOnlyTheOwnerWithoutContact_When_HostAccepts()
    {
        // Act
        await AcceptAsync();

        // Assert
        _notifier.Verify(n => n.NotifyAcceptedAsync(
            It.Is<BookingNotice>(x => x.Email == BookingsTestData.OwnerEmail && x.Recipient == BookingParty.Owner && x.OtherPartyPhone == null && x.Address == null && x.Booking.PaymentDeadline != null),
            It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.NotifyConfirmedAsync(It.IsAny<BookingNotice>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_NotEmail_When_AcceptFails()
    {
        // Act
        await AcceptAsync(BookingsTestData.OtherHostUserId);

        // Assert
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_BookingIsFromAnotherHost()
    {
        // Act
        var result = await AcceptAsync(BookingsTestData.OtherHostUserId);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.NotFound(_booking.Id));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnHostDeactivated_When_HostIsSuspended()
    {
        // Act
        var result = await AcceptAsync(BookingsTestData.SuspendedHostUserId);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.HostDeactivated);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnHostNotFound_When_AccountHasNoHostProfile()
    {
        // Act
        var result = await AcceptAsync(BookingsTestData.NoProfileUserId);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.HostNotFound);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnOwnerInactiveWithoutSavingOrEmailing_When_OwnerWasSuspendedAfterTheRequest()
    {
        // Arrange
        _parties.Setup(p => p.IsOwnerAvailableAsync(BookingBuilder.OwnerId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        // Act
        var result = await AcceptAsync();

        // Assert
        result.FirstError.Should().Be(BookingsErrors.OwnerInactive);
        _booking.Status.Should().Be(BookingStatus.Requested);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_Should_ExpireAndSave_When_DeadlinePassed()
    {
        // Arrange
        _now = BookingBuilder.DefaultNow.AddHours(30);

        // Act
        var result = await AcceptAsync();

        // Assert
        result.FirstError.Should().Be(BookingsErrors.ResponseExpired);
        _booking.Status.Should().Be(BookingStatus.Expired);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.BookingExpired), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_NotSave_When_ListingIsFull()
    {
        // Arrange
        _listings = BookingsTestData.Listings(BookingBuilder.Listing() with { Capacity = 1 });
        var other = new BookingBuilder().WithPets(BookingBuilder.Thor()).BuildConfirmed();
        _repository
            .Setup(r => r.ListOccupyingAsync(BookingBuilder.ListingId, It.IsAny<StayPeriod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([other]);

        // Act
        var result = await AcceptAsync();

        // Assert
        result.FirstError.Should().Be(BookingsErrors.CapacityExceeded(BookingBuilder.DefaultCheckIn));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnListingNotFound_When_ListingVanished()
    {
        // Arrange
        _listings = new Mock<IBookingListings>();

        // Act
        var result = await AcceptAsync();

        // Assert
        result.FirstError.Should().Be(BookingsErrors.ListingNotFound(BookingBuilder.ListingId));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnConflict_When_SomeoneChangedTheBookingMeanwhile()
    {
        // Arrange
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(BookingsErrors.Conflict));

        // Act
        var result = await AcceptAsync();

        // Assert
        result.FirstError.Should().Be(BookingsErrors.Conflict);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

public sealed class DeclineBookingCommandHandlerTests
{
    private readonly Booking _booking = new BookingBuilder().Build();
    private readonly Mock<IBookingsUnitOfWork> _unitOfWork = BookingsTestData.UnitOfWork();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Mock<IBookingNotifier> _notifier = new();
    private DateTimeOffset _now = BookingBuilder.DefaultNow.AddHours(2);

    private DeclineBookingCommandHandler Sut() => new(
        BookingsTestData.Repository(_booking).Object,
        _unitOfWork.Object,
        BookingsTestData.Parties().Object,
        BookingsTestData.ResponseBuilder(),
        BookingsTestData.Notifications(_notifier.Object),
        _auditTrail.Object,
        new FakeTimeProvider(_now));

    [Fact]
    public async Task HandleAsync_Should_DeclineWithReasonAndRecord_When_HostRefuses()
    {
        // Act
        var result = await Sut().HandleAsync(new DeclineBookingCommand(BookingsTestData.HostUserId, _booking.Id.Value, "Vou viajar."), CancellationToken.None);

        // Assert
        result.Value!.Status.Should().Be("declined");
        result.Value.DeclineReason.Should().Be("Vou viajar.");
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.BookingDeclined), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_EmailOnlyTheOwnerWithoutPhone_When_HostRefuses()
    {
        // Act
        await Sut().HandleAsync(new DeclineBookingCommand(BookingsTestData.HostUserId, _booking.Id.Value, "Vou viajar."), CancellationToken.None);

        // Assert
        _notifier.Verify(n => n.NotifyDeclinedAsync(
            It.Is<BookingNotice>(x => x.Email == BookingsTestData.OwnerEmail && x.OtherPartyPhone == null && x.Address == null && x.Booking.DeclineReason == "Vou viajar."),
            It.IsAny<CancellationToken>()), Times.Once);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_Should_ExpireAndReturnResponseExpired_When_DeadlinePassed()
    {
        // Arrange
        _now = BookingBuilder.DefaultNow.AddDays(2);

        // Act
        var result = await Sut().HandleAsync(new DeclineBookingCommand(BookingsTestData.HostUserId, _booking.Id.Value, null), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.ResponseExpired);
        _booking.Status.Should().Be(BookingStatus.Expired);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.BookingExpired), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_NotSave_When_BookingWasAlreadyAnswered()
    {
        // Arrange
        _booking.Accept(BookingBuilder.Listing(), ListingOccupancy.Empty, BookingBuilder.DefaultNow.AddHours(1));

        // Act
        var result = await Sut().HandleAsync(new DeclineBookingCommand(BookingsTestData.HostUserId, _booking.Id.Value, null), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.NotAwaitingResponse);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_HostIsNotTheListingOwner()
    {
        // Act
        var result = await Sut().HandleAsync(new DeclineBookingCommand(BookingsTestData.OtherHostUserId, _booking.Id.Value, null), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.NotFound(_booking.Id));
    }
}

public sealed class DeclineBookingCommandValidatorTests
{
    [Fact]
    public void Validate_Should_RejectReasonLongerThanTheLimit()
    {
        // Act
        var result = new DeclineBookingCommandValidator().Validate(
            new DeclineBookingCommand(Guid.CreateVersion7(), Guid.CreateVersion7(), new string('a', Booking.ReasonMaxLength + 1)));

        // Assert
        result.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Be(BookingsErrors.ReasonTooLong.Message);
    }

    [Fact]
    public void Validate_Should_RequireBookingId()
    {
        // Act
        var result = new AcceptBookingCommandValidator().Validate(new AcceptBookingCommand(Guid.CreateVersion7(), Guid.Empty));

        // Assert
        result.IsValid.Should().BeFalse();
    }
}
