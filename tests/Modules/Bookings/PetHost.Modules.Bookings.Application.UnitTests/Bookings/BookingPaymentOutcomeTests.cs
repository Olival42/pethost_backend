using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.CancelBooking;
using PetHost.Modules.Bookings.Application.Bookings.CancelUnpaidBooking;
using PetHost.Modules.Bookings.Application.Bookings.CheckOutBooking;
using PetHost.Modules.Bookings.Application.Bookings.ConfirmBookingPayment;
using PetHost.Modules.Bookings.Application.Bookings.CreateBooking;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Bookings.Application.UnitTests.Bookings;

public sealed class ConfirmBookingPaymentCommandHandlerTests
{
    private static readonly DateTimeOffset PaidAt = BookingBuilder.DefaultNow.AddHours(3);

    private readonly Mock<IBookingsUnitOfWork> _unitOfWork = BookingsTestData.UnitOfWork();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Mock<IBookingNotifier> _notifier = new();

    private ConfirmBookingPaymentCommandHandler Sut(params Booking[] bookings) => new(
        BookingsTestData.Repository(bookings).Object,
        _unitOfWork.Object,
        BookingsTestData.ResponseBuilder(),
        BookingsTestData.Notifications(_notifier.Object),
        _auditTrail.Object,
        new FakeTimeProvider(PaidAt));

    [Fact]
    public async Task HandleAsync_Should_ConfirmSaveAndRecord_When_BookingIsAccepted()
    {
        // Arrange
        var booking = new BookingBuilder().BuildAccepted();

        // Act
        var result = await Sut(booking).HandleAsync(new ConfirmBookingPaymentCommand(booking.Id.Value), CancellationToken.None);

        // Assert
        result.Value.Should().BeTrue();
        booking.Status.Should().Be(BookingStatus.Confirmed);
        booking.ConfirmedAt.Should().Be(PaidAt);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.BookingConfirmed && r.TargetId == booking.Id.Value), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_EmailBothSidesWithContact_When_BookingIsConfirmed()
    {
        // Arrange
        var booking = new BookingBuilder().BuildAccepted();

        // Act
        await Sut(booking).HandleAsync(new ConfirmBookingPaymentCommand(booking.Id.Value), CancellationToken.None);

        // Assert
        _notifier.Verify(n => n.NotifyConfirmedAsync(
            It.Is<BookingNotice>(x => x.Recipient == BookingParty.Owner && x.OtherPartyPhone == BookingsTestData.HostPhone && x.Address == BookingsTestData.ListingAddress),
            It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.NotifyConfirmedAsync(
            It.Is<BookingNotice>(x => x.Recipient == BookingParty.Host && x.OtherPartyPhone == BookingsTestData.OwnerPhone && x.Address == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnFalseWithoutSaving_When_AlreadyConfirmed()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();

        // Act
        var result = await Sut(booking).HandleAsync(new ConfirmBookingPaymentCommand(booking.Id.Value), CancellationToken.None);

        // Assert
        result.Value.Should().BeFalse();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotAwaitingPayment_When_BookingExpired()
    {
        // Arrange
        var booking = new BookingBuilder().BuildAccepted();
        booking.Expire(booking.PaymentDeadline!.Value.AddMinutes(1));

        // Act
        var result = await Sut(booking).HandleAsync(new ConfirmBookingPaymentCommand(booking.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.NotAwaitingPayment);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_BookingDoesNotExist()
    {
        // Arrange
        var missing = BookingId.New();

        // Act
        var result = await Sut().HandleAsync(new ConfirmBookingPaymentCommand(missing.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.NotFound(missing));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnConflict_When_SaveConflicts()
    {
        // Arrange
        var booking = new BookingBuilder().BuildAccepted();
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(BookingsErrors.Conflict));

        // Act
        var result = await Sut(booking).HandleAsync(new ConfirmBookingPaymentCommand(booking.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.Conflict);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public void Validator_Should_RequireBookingId()
    {
        // Act
        var result = new ConfirmBookingPaymentCommandValidator().Validate(new ConfirmBookingPaymentCommand(Guid.Empty));

        // Assert
        result.Errors.Select(e => e.PropertyName).Should().Equal(nameof(ConfirmBookingPaymentCommand.BookingId));
    }
}

public sealed class CancelUnpaidBookingCommandHandlerTests
{
    private static readonly DateTimeOffset At = BookingClock.StartOf(BookingBuilder.DefaultCheckIn).AddHours(-12);

    private readonly Mock<IBookingsUnitOfWork> _unitOfWork = BookingsTestData.UnitOfWork();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Mock<IBookingNotifier> _notifier = new();

    private CancelUnpaidBookingCommandHandler Sut(params Booking[] bookings) => new(
        BookingsTestData.Repository(bookings).Object,
        _unitOfWork.Object,
        BookingsTestData.ResponseBuilder(),
        BookingsTestData.Notifications(_notifier.Object),
        _auditTrail.Object,
        new FakeTimeProvider(At));

    [Fact]
    public async Task HandleAsync_Should_CancelByThePlatformRecordAndEmailBoth_When_Confirmed()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();

        // Act
        var result = await Sut(booking).HandleAsync(new CancelUnpaidBookingCommand(booking.Id.Value), CancellationToken.None);

        // Assert
        result.Value.Should().BeTrue();
        booking.CancelledBy.Should().Be(BookingParty.Platform);
        booking.RefundPercentage.Should().Be(100);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.BookingCancelled && r.Details!["by"] == "platform" && r.Details["refundPercentage"] == "100"),
            It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.NotifyCancelledAsync(It.IsAny<BookingNotice>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnFalseWithoutSaving_When_AlreadyCancelled()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();
        booking.Cancel(BookingParty.Host, null, At.AddHours(-1));

        // Act
        var result = await Sut(booking).HandleAsync(new CancelUnpaidBookingCommand(booking.Id.Value), CancellationToken.None);

        // Assert
        result.Value.Should().BeFalse();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnCannotCancelAfterCheckIn_When_PetAlreadyArrived()
    {
        // Arrange
        var booking = new BookingBuilder().BuildInProgress();

        // Act
        var result = await Sut(booking).HandleAsync(new CancelUnpaidBookingCommand(booking.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.CannotCancelAfterCheckIn);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_BookingDoesNotExist()
    {
        // Arrange
        var missing = BookingId.New();

        // Act
        var result = await Sut().HandleAsync(new CancelUnpaidBookingCommand(missing.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.NotFound(missing));
    }

    [Fact]
    public void Validator_Should_RequireBookingId()
    {
        // Act
        var result = new CancelUnpaidBookingCommandValidator().Validate(new CancelUnpaidBookingCommand(Guid.Empty));

        // Assert
        result.Errors.Select(e => e.PropertyName).Should().Equal(nameof(CancelUnpaidBookingCommand.BookingId));
    }
}

/// <summary>Onde o Bookings conversa com o pagamento: tutor devendo, cancelamento e check-out.</summary>
public sealed class BookingPaymentsPortTests
{
    [Fact]
    public async Task Create_Should_ReturnOwnerPaymentOverdue_When_OwnerHasAnOverdueInstallment()
    {
        // Arrange
        var repository = BookingsTestData.Repository();
        var unitOfWork = BookingsTestData.UnitOfWork();
        var sut = new CreateBookingCommandHandler(
            repository.Object,
            unitOfWork.Object,
            BookingsTestData.Parties().Object,
            BookingsTestData.Listings().Object,
            BookingsTestData.Pets().Object,
            BookingsTestData.Payments(ownerHasOverduePayment: true).Object,
            BookingsTestData.ResponseBuilder(),
            new Mock<IAuditTrail>().Object,
            new FakeTimeProvider(BookingBuilder.DefaultNow));

        // Act
        var result = await sut.HandleAsync(
            new CreateBookingCommand(BookingsTestData.OwnerUserId, BookingBuilder.ListingId, [BookingBuilder.PipocaId], "2026-10-20", "2026-10-25"),
            CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.OwnerPaymentOverdue);
        repository.Verify(r => r.Add(It.IsAny<Booking>()), Times.Never);
        unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_Should_NotifyPayments_When_BookingIsCancelled()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();
        var payments = BookingsTestData.Payments();
        var sut = new CancelBookingCommandHandler(
            BookingsTestData.Repository(booking).Object,
            BookingsTestData.UnitOfWork().Object,
            BookingsTestData.Parties().Object,
            payments.Object,
            BookingsTestData.ResponseBuilder(),
            BookingsTestData.Notifications(),
            new Mock<IAuditTrail>().Object,
            new FakeTimeProvider(BookingBuilder.DefaultNow.AddHours(5)));

        // Act
        await sut.HandleAsync(new CancelBookingCommand(BookingsTestData.HostUserId, Roles.Host, booking.Id.Value, null), CancellationToken.None);

        // Assert
        payments.Verify(p => p.BookingChangedAsync(booking.Id.Value, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_Should_NotNotifyPayments_When_CancelFails()
    {
        // Arrange
        var booking = new BookingBuilder().BuildInProgress();
        var payments = BookingsTestData.Payments();
        var sut = new CancelBookingCommandHandler(
            BookingsTestData.Repository(booking).Object,
            BookingsTestData.UnitOfWork().Object,
            BookingsTestData.Parties().Object,
            payments.Object,
            BookingsTestData.ResponseBuilder(),
            BookingsTestData.Notifications(),
            new Mock<IAuditTrail>().Object,
            new FakeTimeProvider(BookingClock.StartOf(BookingBuilder.DefaultCheckIn).AddHours(12)));

        // Act
        var result = await sut.HandleAsync(new CancelBookingCommand(BookingsTestData.OwnerUserId, Roles.Owner, booking.Id.Value, null), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.CannotCancelAfterCheckIn);
        payments.Verify(p => p.BookingChangedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckOut_Should_NotifyPaymentsToReleaseThePayout_When_StayIsCompleted()
    {
        // Arrange
        var booking = new BookingBuilder().BuildInProgress();
        var payments = BookingsTestData.Payments();
        var sut = new CheckOutBookingCommandHandler(
            BookingsTestData.Repository(booking).Object,
            BookingsTestData.UnitOfWork().Object,
            BookingsTestData.Parties().Object,
            payments.Object,
            BookingsTestData.ResponseBuilder(),
            new Mock<IAuditTrail>().Object,
            new FakeTimeProvider(BookingClock.StartOf(BookingBuilder.DefaultCheckOut).AddHours(10)));

        // Act
        await sut.HandleAsync(new CheckOutBookingCommand(BookingsTestData.HostUserId, booking.Id.Value), CancellationToken.None);

        // Assert
        booking.Status.Should().Be(BookingStatus.Completed);
        payments.Verify(p => p.BookingChangedAsync(booking.Id.Value, It.IsAny<CancellationToken>()), Times.Once);
    }
}
