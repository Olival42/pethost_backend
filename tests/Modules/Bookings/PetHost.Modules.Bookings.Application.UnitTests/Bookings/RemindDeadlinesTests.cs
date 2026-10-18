using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.RemindDeadlines;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Bookings.Application.UnitTests.Bookings;

/// <summary>A rodada dos lembretes de prazo: pedido sem resposta e 1ª parcela sem pagamento.</summary>
public sealed class RemindDeadlinesTests
{
    private readonly Mock<IBookingsUnitOfWork> _unitOfWork = BookingsTestData.UnitOfWork();
    private readonly Mock<IBookingNotifier> _notifier = new();

    [Fact]
    public async Task HandleAsync_Should_RemindOnlyTheHost_When_ARequestIsCloseToExpiring()
    {
        // Arrange
        var booking = new BookingBuilder().Build();

        // Act
        var result = await Sut(booking.ResponseDeadline.AddHours(-2), booking).HandleAsync(new RemindDeadlinesCommand(), CancellationToken.None);

        // Assert
        result.Value.Should().Be(new RemindDeadlinesResponse(1, 0));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(
            n => n.NotifyResponseReminderAsync(
                It.Is<BookingNotice>(x => x.Recipient == BookingParty.Host
                    && x.Email == BookingsTestData.HostEmail
                    && x.OtherPartyPhone == null
                    && x.Booking.ResponseDeadline == booking.ResponseDeadline),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_Should_RemindOnlyTheOwner_When_TheFirstInstallmentIsCloseToTheDeadline()
    {
        // Arrange
        var booking = new BookingBuilder().BuildAccepted();

        // Act
        var result = await Sut(booking.PaymentDeadline!.Value.AddHours(-3), booking).HandleAsync(new RemindDeadlinesCommand(), CancellationToken.None);

        // Assert
        result.Value.Should().Be(new RemindDeadlinesResponse(0, 1));
        _notifier.Verify(
            n => n.NotifyPaymentReminderAsync(
                It.Is<BookingNotice>(x => x.Recipient == BookingParty.Owner
                    && x.Email == BookingsTestData.OwnerEmail
                    && x.OtherPartyPhone == null
                    && x.Booking.PaymentDeadline == booking.PaymentDeadline),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_Should_SaveAndSendNothing_When_NoDeadlineIsClose()
    {
        // Act — o repositório devolveu as duas, mas ainda falta muito para os prazos.
        var result = await Sut(BookingBuilder.DefaultNow.AddHours(2), new BookingBuilder().Build(), new BookingBuilder().BuildAccepted())
            .HandleAsync(new RemindDeadlinesCommand(), CancellationToken.None);

        // Assert
        result.Value.Should().Be(new RemindDeadlinesResponse(0, 0));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _notifier.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_Should_SendNoEmail_When_SavingTheRoundFails()
    {
        // Arrange
        var booking = new BookingBuilder().Build();
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PetHost.Shared.Kernel.Results.Result.Failure(PetHost.Modules.Bookings.Domain.Errors.BookingsErrors.Conflict));

        // Act
        var result = await Sut(booking.ResponseDeadline.AddHours(-1), booking).HandleAsync(new RemindDeadlinesCommand(), CancellationToken.None);

        // Assert
        result.IsFailure.Should().BeTrue();
        _notifier.VerifyNoOtherCalls();
    }

    private RemindDeadlinesCommandHandler Sut(DateTimeOffset now, params Booking[] candidates)
    {
        var repository = BookingsTestData.Repository(candidates);
        repository.Setup(r => r.ListDeadlinesToRemindAsync(It.IsAny<DateTimeOffset>(), RemindDeadlinesCommand.BatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync(candidates);

        return new RemindDeadlinesCommandHandler(
            repository.Object,
            _unitOfWork.Object,
            BookingsTestData.ResponseBuilder(),
            BookingsTestData.Notifications(_notifier.Object),
            new FakeTimeProvider(now));
    }
}
