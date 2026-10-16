using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.RemindUpcomingStays;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Bookings.Application.UnitTests.Bookings;

/// <summary>A rodada do lembrete da véspera do check-in.</summary>
public sealed class RemindUpcomingStaysTests
{
    private static readonly DateTimeOffset Eve = BookingClock.StartOf(BookingBuilder.DefaultCheckIn.AddDays(-1)).AddHours(10);

    private readonly Mock<IBookingsUnitOfWork> _unitOfWork = BookingsTestData.UnitOfWork();
    private readonly Mock<IBookingNotifier> _notifier = new();

    [Fact]
    public async Task HandleAsync_Should_RemindBothSidesWithTheirContacts_When_ItIsTheEve()
    {
        // Arrange
        var booking = new BookingBuilder().BuildConfirmed();

        // Act
        var result = await Sut(Eve, booking).HandleAsync(new RemindUpcomingStaysCommand(), CancellationToken.None);

        // Assert
        result.Value.Should().Be(1);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(
            n => n.NotifyCheckInReminderAsync(
                It.Is<BookingNotice>(x => x.Recipient == BookingParty.Owner
                    && x.OtherPartyPhone == BookingsTestData.HostPhone
                    && x.Address == BookingsTestData.ListingAddress),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _notifier.Verify(
            n => n.NotifyCheckInReminderAsync(
                It.Is<BookingNotice>(x => x.Recipient == BookingParty.Host && x.OtherPartyPhone == BookingsTestData.OwnerPhone && x.Address == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_SaveAndSendNothing_When_NoBookingIsOnTheEve()
    {
        // Act — dois dias antes: o repositório devolveu a reserva, mas não é a véspera.
        var result = await Sut(Eve.AddDays(-1), new BookingBuilder().BuildConfirmed()).HandleAsync(new RemindUpcomingStaysCommand(), CancellationToken.None);

        // Assert
        result.Value.Should().Be(0);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _notifier.VerifyNoOtherCalls();
    }

    private RemindUpcomingStaysCommandHandler Sut(DateTimeOffset now, params Booking[] upcoming)
    {
        var repository = BookingsTestData.Repository(upcoming);
        repository.Setup(r => r.ListCheckInsToRemindAsync(It.IsAny<DateOnly>(), RemindUpcomingStaysCommand.BatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(upcoming);

        return new RemindUpcomingStaysCommandHandler(
            repository.Object,
            _unitOfWork.Object,
            BookingsTestData.ResponseBuilder(),
            BookingsTestData.Notifications(_notifier.Object),
            new FakeTimeProvider(now));
    }
}
