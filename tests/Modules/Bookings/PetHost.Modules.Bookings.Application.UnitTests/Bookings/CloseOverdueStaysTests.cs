using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.CloseOverdueStays;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Bookings.Application.UnitTests.Bookings;

/// <summary>A rodada das estadias que já deviam ter acabado.</summary>
public sealed class CloseOverdueStaysTests
{
    private readonly Mock<IBookingsUnitOfWork> _unitOfWork = BookingsTestData.UnitOfWork();
    private readonly Mock<IBookingPayments> _payments = BookingsTestData.Payments();
    private readonly Mock<IBookingNotifier> _notifier = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();

    private static DateTimeOffset DaysAfterCheckOut(int days) =>
        BookingClock.StartOf(BookingBuilder.DefaultCheckOut.AddDays(days)).AddHours(10);

    [Fact]
    public async Task HandleAsync_Should_CancelTheNoShowAndTellPaymentsAndBothSides_When_ThePetNeverArrived()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();

        // Act
        var result = await Sut(DaysAfterCheckOut(0), booking).HandleAsync(new CloseOverdueStaysCommand(), CancellationToken.None);

        // Assert
        result.Value.Should().Be(new CloseOverdueStaysResponse(1, 0, 0));
        booking.RefundPercentage.Should().Be(50);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _payments.Verify(p => p.BookingChangedAsync(booking.Id.Value, It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(
            a => a.RecordAsync(
                It.Is<AuditRecord>(r => r.Action == AuditActions.BookingCancelled
                    && r.Details!["by"] == "platform"
                    && r.Details["refundPercentage"] == "50"
                    && r.Details["cause"] == CloseOverdueStaysCommandHandler.NoShowCause),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _notifier.Verify(n => n.NotifyCancelledAsync(It.IsAny<BookingNotice>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task HandleAsync_Should_OnlyEmailTheHost_When_ItIsTheCheckOutDayWithoutCheckOut()
    {
        // Arrange
        var booking = new BookingBuilder().BuildInProgress();

        // Act
        var result = await Sut(DaysAfterCheckOut(0), booking).HandleAsync(new CloseOverdueStaysCommand(), CancellationToken.None);

        // Assert
        result.Value.Should().Be(new CloseOverdueStaysResponse(0, 1, 0));
        booking.Status.Should().Be(BookingStatus.InProgress);
        _notifier.Verify(
            n => n.NotifyCheckOutReminderAsync(It.Is<BookingNotice>(x => x.Recipient == BookingParty.Host && x.Email == BookingsTestData.HostEmail), It.IsAny<CancellationToken>()),
            Times.Once);
        _notifier.VerifyNoOtherCalls();
        _payments.Verify(p => p.BookingChangedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_CompleteTheStayReleaseThePayoutAndTellBothSides_When_TheGraceIsOver()
    {
        // Arrange
        var booking = new BookingBuilder().BuildInProgress();

        // Act
        var result = await Sut(DaysAfterCheckOut(Booking.AutoCheckOutAfterDays), booking).HandleAsync(new CloseOverdueStaysCommand(), CancellationToken.None);

        // Assert
        result.Value.Should().Be(new CloseOverdueStaysResponse(0, 0, 1));
        booking.Status.Should().Be(BookingStatus.Completed);
        _payments.Verify(p => p.BookingChangedAsync(booking.Id.Value, It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(
            a => a.RecordAsync(
                It.Is<AuditRecord>(r => r.Action == AuditActions.BookingCheckedOut
                    && r.Details!["by"] == "platform"
                    && r.Details["cause"] == CloseOverdueStaysCommandHandler.AutoCheckOutCause),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _notifier.Verify(n => n.NotifyAutoCheckedOutAsync(It.Is<BookingNotice>(x => x.Recipient == BookingParty.Owner), It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.NotifyAutoCheckedOutAsync(It.Is<BookingNotice>(x => x.Recipient == BookingParty.Host), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_SaveNothing_When_NoStayIsOverdue()
    {
        // Act
        var result = await Sut(DaysAfterCheckOut(-1), new BookingBuilder().BuildConfirmed()).HandleAsync(new CloseOverdueStaysCommand(), CancellationToken.None);

        // Assert
        result.Value.Should().Be(new CloseOverdueStaysResponse(0, 0, 0));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_AnnounceNothing_When_TheSaveConflicts()
    {
        // Arrange
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(BookingsErrors.Conflict));

        // Act
        var result = await Sut(DaysAfterCheckOut(0), new BookingBuilder().BuildConfirmed()).HandleAsync(new CloseOverdueStaysCommand(), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.Conflict);
        _payments.Verify(p => p.BookingChangedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifier.VerifyNoOtherCalls();
    }

    private CloseOverdueStaysCommandHandler Sut(DateTimeOffset now, params Booking[] overdue)
    {
        var repository = BookingsTestData.Repository(overdue);
        repository.Setup(r => r.ListOverdueStaysAsync(BookingClock.Today(now), CloseOverdueStaysCommand.BatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(overdue);

        return new CloseOverdueStaysCommandHandler(
            repository.Object,
            _unitOfWork.Object,
            _payments.Object,
            BookingsTestData.ResponseBuilder(),
            BookingsTestData.Notifications(_notifier.Object),
            _auditTrail.Object,
            new FakeTimeProvider(now));
    }
}
