using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.CancelBooking;
using PetHost.Modules.Bookings.Application.Bookings.CheckInBooking;
using PetHost.Modules.Bookings.Application.Bookings.CheckOutBooking;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Bookings.Application.UnitTests.Bookings;

public sealed class CancelBookingCommandHandlerTests
{
    private static readonly DateTimeOffset CheckInStart = BookingClock.StartOf(BookingBuilder.DefaultCheckIn);

    private readonly Mock<IBookingsUnitOfWork> _unitOfWork = BookingsTestData.UnitOfWork();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Mock<IBookingNotifier> _notifier = new();

    private CancelBookingCommandHandler Sut(Booking booking, DateTimeOffset now) => new(
        BookingsTestData.Repository(booking).Object,
        _unitOfWork.Object,
        BookingsTestData.Parties().Object,
        BookingsTestData.ResponseBuilder(),
        BookingsTestData.Notifications(_notifier.Object),
        _auditTrail.Object,
        new FakeTimeProvider(now));

    [Fact]
    public async Task HandleAsync_Should_CancelAsOwnerWithoutRefund_When_NotPaidYet()
    {
        // Arrange
        var booking = new BookingBuilder().Build();

        // Act
        var result = await Sut(booking, BookingBuilder.DefaultNow.AddHours(1))
            .HandleAsync(new CancelBookingCommand(BookingsTestData.OwnerUserId, Roles.Owner, booking.Id.Value, "Mudei de planos"), CancellationToken.None);

        // Assert
        result.Value!.Status.Should().Be("cancelled");
        result.Value.CancelledBy.Should().Be("owner");
        result.Value.RefundPercentage.Should().BeNull();
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.BookingCancelled && r.Details!["by"] == "owner"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_EmailBothSides_When_HostCancels()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();

        // Act
        await Sut(booking, CheckInStart.AddDays(-3))
            .HandleAsync(new CancelBookingCommand(BookingsTestData.HostUserId, Roles.Host, booking.Id.Value, "Imprevisto"), CancellationToken.None);

        // Assert
        _notifier.Verify(n => n.NotifyCancelledAsync(
            It.Is<BookingNotice>(x => x.Email == BookingsTestData.OwnerEmail && x.Recipient == BookingParty.Owner && x.Booking.CancelledBy == "host"),
            It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.NotifyCancelledAsync(
            It.Is<BookingNotice>(x => x.Email == BookingsTestData.HostEmail && x.Recipient == BookingParty.Host),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_RefundHalf_When_OwnerCancelsAConfirmedStayLate()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();

        // Act
        var result = await Sut(booking, CheckInStart.AddHours(-10))
            .HandleAsync(new CancelBookingCommand(BookingsTestData.OwnerUserId, Roles.Owner, booking.Id.Value, null), CancellationToken.None);

        // Assert
        result.Value!.RefundPercentage.Should().Be(50);
    }

    [Fact]
    public async Task HandleAsync_Should_RefundEverything_When_HostCancelsAConfirmedStay()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();

        // Act
        var result = await Sut(booking, CheckInStart.AddHours(-10))
            .HandleAsync(new CancelBookingCommand(BookingsTestData.HostUserId, Roles.Host, booking.Id.Value, null), CancellationToken.None);

        // Assert
        result.Value!.CancelledBy.Should().Be("host");
        result.Value.RefundPercentage.Should().Be(100);
    }

    [Fact]
    public async Task HandleAsync_Should_AllowInactiveHostToCancel()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();

        // Act
        var result = await Sut(booking, BookingBuilder.DefaultNow.AddDays(1))
            .HandleAsync(new CancelBookingCommand(BookingsTestData.SuspendedHostUserId, Roles.Host, booking.Id.Value, null), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Host)]
    public async Task HandleAsync_Should_ReturnNotFound_When_BookingBelongsToSomeoneElse(string role)
    {
        // Arrange
        var booking = new BookingBuilder().Build();
        var stranger = role == Roles.Owner ? BookingsTestData.OtherOwnerUserId : BookingsTestData.OtherHostUserId;

        // Act
        var result = await Sut(booking, BookingBuilder.DefaultNow.AddHours(1))
            .HandleAsync(new CancelBookingCommand(stranger, role, booking.Id.Value, null), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.NotFound(booking.Id));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnOwnerNotFound_When_AccountHasNoOwnerProfile()
    {
        // Arrange
        var booking = new BookingBuilder().Build();

        // Act
        var result = await Sut(booking, BookingBuilder.DefaultNow)
            .HandleAsync(new CancelBookingCommand(BookingsTestData.NoProfileUserId, Roles.Owner, booking.Id.Value, null), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.OwnerNotFound);
    }

    [Fact]
    public async Task HandleAsync_Should_NotSave_When_PetAlreadyArrived()
    {
        // Arrange
        var booking = new BookingBuilder().BuildInProgress();

        // Act
        var result = await Sut(booking, CheckInStart.AddHours(20))
            .HandleAsync(new CancelBookingCommand(BookingsTestData.OwnerUserId, Roles.Owner, booking.Id.Value, null), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.CannotCancelAfterCheckIn);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_NotRecord_When_SaveConflicts()
    {
        // Arrange
        var booking = new BookingBuilder().Build();
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(BookingsErrors.Conflict));

        // Act
        var result = await Sut(booking, BookingBuilder.DefaultNow)
            .HandleAsync(new CancelBookingCommand(BookingsTestData.OwnerUserId, Roles.Owner, booking.Id.Value, null), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.Conflict);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

public sealed class CheckInBookingCommandHandlerTests
{
    private static readonly DateTimeOffset CheckInStart = BookingClock.StartOf(BookingBuilder.DefaultCheckIn);

    private readonly Booking _booking = new BookingBuilder().BuildConfirmed();
    private readonly Mock<IBookingsUnitOfWork> _unitOfWork = BookingsTestData.UnitOfWork();
    private readonly Mock<IAuditTrail> _auditTrail = new();

    private CheckInBookingCommandHandler Sut(DateTimeOffset now) => new(
        BookingsTestData.Repository(_booking).Object,
        _unitOfWork.Object,
        BookingsTestData.Parties().Object,
        BookingsTestData.ResponseBuilder(),
        _auditTrail.Object,
        new FakeTimeProvider(now));

    [Fact]
    public async Task HandleAsync_Should_StartTheStayAndRecord_When_HostChecksInOnTheDay()
    {
        // Act
        var result = await Sut(CheckInStart.AddHours(14)).HandleAsync(new CheckInBookingCommand(BookingsTestData.HostUserId, _booking.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.Status.Should().Be("in_progress");
        result.Value.CheckedInAt.Should().Be(CheckInStart.AddHours(14));
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.BookingCheckedIn), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_NotSave_When_TooEarly()
    {
        // Act
        var result = await Sut(CheckInStart.AddDays(-1)).HandleAsync(new CheckInBookingCommand(BookingsTestData.HostUserId, _booking.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.CheckInTooEarly(BookingBuilder.DefaultCheckIn));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_HostIsNotTheListingOwner()
    {
        // Act
        var result = await Sut(CheckInStart.AddHours(14)).HandleAsync(new CheckInBookingCommand(BookingsTestData.OtherHostUserId, _booking.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.NotFound(_booking.Id));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnConflict_When_SaveConflicts()
    {
        // Arrange
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(BookingsErrors.Conflict));

        // Act
        var result = await Sut(CheckInStart.AddHours(14)).HandleAsync(new CheckInBookingCommand(BookingsTestData.HostUserId, _booking.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.Conflict);
    }
}

public sealed class CheckOutBookingCommandHandlerTests
{
    private static readonly DateTimeOffset CheckOutDay = BookingClock.StartOf(BookingBuilder.DefaultCheckOut).AddHours(10);

    private readonly Mock<IBookingsUnitOfWork> _unitOfWork = BookingsTestData.UnitOfWork();
    private readonly Mock<IAuditTrail> _auditTrail = new();

    private CheckOutBookingCommandHandler Sut(Booking booking) => new(
        BookingsTestData.Repository(booking).Object,
        _unitOfWork.Object,
        BookingsTestData.Parties().Object,
        BookingsTestData.ResponseBuilder(),
        _auditTrail.Object,
        new FakeTimeProvider(CheckOutDay));

    [Fact]
    public async Task HandleAsync_Should_CompleteTheStayAndRecord_When_PetIsWithTheHost()
    {
        // Arrange
        var booking = new BookingBuilder().BuildInProgress();

        // Act
        var result = await Sut(booking).HandleAsync(new CheckOutBookingCommand(BookingsTestData.HostUserId, booking.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.Status.Should().Be("completed");
        result.Value.CheckedOutAt.Should().Be(CheckOutDay);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.BookingCheckedOut), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_NotSave_When_PetNeverArrived()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();

        // Act
        var result = await Sut(booking).HandleAsync(new CheckOutBookingCommand(BookingsTestData.HostUserId, booking.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.NotInProgress);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnHostDeactivated_When_HostIsSuspended()
    {
        // Arrange
        var booking = new BookingBuilder().BuildInProgress();

        // Act
        var result = await Sut(booking).HandleAsync(new CheckOutBookingCommand(BookingsTestData.SuspendedHostUserId, booking.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.HostDeactivated);
    }
}
