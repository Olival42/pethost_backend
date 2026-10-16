using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.AcceptBooking;
using PetHost.Modules.Bookings.Application.Bookings.CancelBooking;
using PetHost.Modules.Bookings.Application.Bookings.CancelBookingsForSuspension;
using PetHost.Modules.Bookings.Application.Bookings.CheckOutBooking;
using PetHost.Modules.Bookings.Application.Bookings.CreateBooking;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;
using static PetHost.Modules.Bookings.Application.UnitTests.Bookings.BookingsTestData;

namespace PetHost.Modules.Bookings.Application.UnitTests.Bookings;

/// <summary>
/// O que a plataforma faz na reserva: datas fechadas na agenda, cancelamento quando o admin
/// suspende um lado, cancelamento e check-out pelo admin, e o aceite de cantinho que não pode
/// mais receber reservas.
/// </summary>
public sealed class PlatformActionsTests
{
    private static readonly Guid AdminUserId = Guid.CreateVersion7();

    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Mock<IBookingsUnitOfWork> _unitOfWork = UnitOfWork();
    private readonly Mock<IBookingPayments> _payments = BookingsTestData.Payments();
    private readonly Mock<IBookingNotifier> _notifier = new();
    private readonly FakeTimeProvider _time = new(BookingBuilder.DefaultNow.AddDays(1));

    // ----- agenda -----

    [Fact]
    public async Task Create_Should_FailWithDatesBlocked_When_TheHostClosedADayOfTheStay()
    {
        // Arrange
        var listing = BookingBuilder.Listing() with { ClosedPeriods = [new ClosedPeriod(new DateOnly(2026, 10, 24), new DateOnly(2026, 10, 31))] };
        var repository = Repository();
        var handler = new CreateBookingCommandHandler(
            repository.Object, _unitOfWork.Object, Parties().Object, BookingsTestData.Listings(listing).Object, BookingsTestData.Pets().Object, _payments.Object, ResponseBuilder(), Notifications(_notifier.Object), _auditTrail.Object, _time);

        // Act
        var result = await handler.HandleAsync(
            new CreateBookingCommand(OwnerUserId, BookingBuilder.ListingId, [BookingBuilder.PipocaId], "2026-10-20", "2026-10-25"),
            CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.DatesBlocked(new DateOnly(2026, 10, 24)));
        repository.Verify(r => r.Add(It.IsAny<Booking>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ----- aceite -----

    [Fact]
    public async Task Accept_Should_FailWithoutSaving_When_TheListingCanNoLongerTakeBookings()
    {
        // Arrange — o Stripe bloqueou os repasses da anfitriã depois do pedido.
        var booking = new BookingBuilder().Build();
        var handler = new AcceptBookingCommandHandler(
            Repository(booking).Object,
            _unitOfWork.Object,
            Parties().Object,
            BookingsTestData.Listings(BookingBuilder.Listing() with { IsBookable = false }).Object,
            ResponseBuilder(),
            Notifications(_notifier.Object),
            _auditTrail.Object,
            _time);

        // Act
        var result = await handler.HandleAsync(new AcceptBookingCommand(HostUserId, booking.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.ListingNotBookable);
        booking.Status.Should().Be(BookingStatus.Requested);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _notifier.Verify(n => n.NotifyAcceptedAsync(It.IsAny<BookingNotice>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ----- suspensão -----

    [Fact]
    public async Task CancelForSuspension_Should_CancelEveryBookingNotStarted_When_TheHostIsSuspended()
    {
        // Arrange
        var requested = new BookingBuilder().Build();
        var confirmed = new BookingBuilder().WithPeriod(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3)).BuildConfirmed();
        var repository = Repository(requested, confirmed);
        repository.Setup(r => r.ListCancellableIdsAsync(BookingParty.Host, BookingBuilder.HostId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([requested.Id, confirmed.Id]);

        // Act
        var result = await Suspension(repository).HandleAsync(
            new CancelBookingsForSuspensionCommand(BookingParty.Host, BookingBuilder.HostId),
            CancellationToken.None);

        // Assert
        result.Value!.Cancelled.Should().Be(2);
        result.Value.NotCancelled.Should().BeEmpty();
        requested.CancelledBy.Should().Be(BookingParty.Platform);
        confirmed.RefundPercentage.Should().Be(Booking.FullRefundPercentage);
        _payments.Verify(p => p.BookingChangedAsync(confirmed.Id.Value, It.IsAny<CancellationToken>()), Times.Once);
        _payments.Verify(p => p.BookingChangedAsync(requested.Id.Value, It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(
            a => a.RecordAsync(
                It.Is<AuditRecord>(r => r.Action == AuditActions.BookingCancelled
                    && r.Details!["by"] == "platform"
                    && r.Details["cause"] == CancelBookingsForSuspensionCommandHandler.HostSuspendedCause),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // Os dois lados recebem o e-mail de cada reserva.
        _notifier.Verify(n => n.NotifyCancelledAsync(It.Is<BookingNotice>(x => x.Recipient == BookingParty.Owner), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _notifier.Verify(n => n.NotifyCancelledAsync(It.Is<BookingNotice>(x => x.Recipient == BookingParty.Host), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CancelForSuspension_Should_LeaveThePaidAmountToTheHost_When_TheOwnerIsSuspended()
    {
        // Arrange
        var confirmed = new BookingBuilder().BuildConfirmed();
        var repository = Repository(confirmed);
        repository.Setup(r => r.ListCancellableIdsAsync(BookingParty.Owner, BookingBuilder.OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([confirmed.Id]);

        // Act
        var result = await Suspension(repository).HandleAsync(
            new CancelBookingsForSuspensionCommand(BookingParty.Owner, BookingBuilder.OwnerId),
            CancellationToken.None);

        // Assert
        result.Value!.Cancelled.Should().Be(1);
        confirmed.RefundPercentage.Should().Be(Booking.NoRefundPercentage);
        confirmed.CancellationReason.Should().Be(Booking.OwnerSuspendedReason);
    }

    [Fact]
    public async Task CancelForSuspension_Should_RetryOnceAndReportTheBooking_When_TheSaveKeepsConflicting()
    {
        // Arrange — cada leitura traz a reserva de novo do banco (o conflito limpa o rastreamento).
        var id = new BookingBuilder().BuildConfirmed().Id;
        var repository = Repository();
        repository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(() => new BookingBuilder().BuildConfirmed());
        repository.Setup(r => r.ListCancellableIdsAsync(BookingParty.Host, BookingBuilder.HostId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([id]);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(BookingsErrors.Conflict));

        // Act
        var result = await Suspension(repository).HandleAsync(
            new CancelBookingsForSuspensionCommand(BookingParty.Host, BookingBuilder.HostId),
            CancellationToken.None);

        // Assert
        result.Value!.Cancelled.Should().Be(0);
        result.Value.NotCancelled.Should().Equal(id.Value);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        _payments.Verify(p => p.BookingChangedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifier.Verify(n => n.NotifyCancelledAsync(It.IsAny<BookingNotice>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void CancelForSuspensionValidator_Should_RejectThePlatform_When_ItIsTheSuspendedSide()
    {
        // Act
        var result = new CancelBookingsForSuspensionCommandValidator().Validate(new CancelBookingsForSuspensionCommand(BookingParty.Platform, Guid.CreateVersion7()));

        // Assert
        result.IsValid.Should().BeFalse();
    }

    // ----- admin -----

    [Fact]
    public async Task Cancel_Should_CancelAsThePlatformWithFullRefundAndTheReasonInTheTrail_When_AdminCancels()
    {
        // Arrange — no dia da chegada (o tutor levaria 50%).
        var booking = new BookingBuilder().BuildConfirmed();
        _time.SetUtcNow(BookingClock.StartOf(BookingBuilder.DefaultCheckIn).AddHours(8));
        var handler = new CancelBookingCommandHandler(
            Repository(booking).Object, _unitOfWork.Object, Parties().Object, _payments.Object, ResponseBuilder(), Notifications(_notifier.Object), _auditTrail.Object, _time);

        // Act
        var result = await handler.HandleAsync(
            new CancelBookingCommand(AdminUserId, Roles.Admin, booking.Id.Value, "Repasses do anfitrião bloqueados no Stripe"),
            CancellationToken.None);

        // Assert
        result.Value!.CancelledBy.Should().Be("platform");
        result.Value.RefundPercentage.Should().Be(100);
        result.Value.Contact.Should().BeNull();
        _payments.Verify(p => p.BookingChangedAsync(booking.Id.Value, It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(
            a => a.RecordAsync(
                It.Is<AuditRecord>(r => r.Action == AuditActions.BookingCancelled && r.Reason == "Repasses do anfitrião bloqueados no Stripe"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _notifier.Verify(n => n.NotifyCancelledAsync(It.IsAny<BookingNotice>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void CancelValidator_Should_RequireAReason_When_AdminCancels(string? reason)
    {
        // Act
        var admin = new CancelBookingCommandValidator().Validate(new CancelBookingCommand(AdminUserId, Roles.Admin, Guid.CreateVersion7(), reason));
        var owner = new CancelBookingCommandValidator().Validate(new CancelBookingCommand(OwnerUserId, Roles.Owner, Guid.CreateVersion7(), reason));

        // Assert
        admin.Errors.Should().ContainSingle(e => e.ErrorMessage == BookingsErrors.AdminReasonRequired.Message);
        owner.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task CheckOut_Should_CompleteTheStayOfAnyHost_When_AdminChecksOut()
    {
        // Arrange — a anfitriã foi suspensa e não entra mais.
        var booking = new BookingBuilder().BuildInProgress();
        var handler = new CheckOutBookingCommandHandler(
            Repository(booking).Object, _unitOfWork.Object, Parties().Object, _payments.Object, ResponseBuilder(), _auditTrail.Object, _time);

        // Act
        var result = await handler.HandleAsync(
            new CheckOutBookingCommand(AdminUserId, booking.Id.Value, Roles.Admin, "Anfitriã suspensa; tutora buscou a Pipoca"),
            CancellationToken.None);

        // Assert
        result.Value!.Status.Should().Be("completed");
        _payments.Verify(p => p.BookingChangedAsync(booking.Id.Value, It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(
            a => a.RecordAsync(
                It.Is<AuditRecord>(r => r.Action == AuditActions.BookingCheckedOut
                    && r.Reason == "Anfitriã suspensa; tutora buscou a Pipoca"
                    && r.Details!["by"] == "platform"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CheckOut_Should_ReturnNotFound_When_TheAdminAsksForAnUnknownBooking()
    {
        // Arrange
        var handler = new CheckOutBookingCommandHandler(
            Repository().Object, _unitOfWork.Object, Parties().Object, _payments.Object, ResponseBuilder(), _auditTrail.Object, _time);

        // Act
        var result = await handler.HandleAsync(new CheckOutBookingCommand(AdminUserId, Guid.CreateVersion7(), Roles.Admin, "x"), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("BOOKING_NOT_FOUND");
    }

    [Fact]
    public void CheckOutValidator_Should_RequireAReasonOnlyFromTheAdmin_When_Missing()
    {
        // Act
        var admin = new CheckOutBookingCommandValidator().Validate(new CheckOutBookingCommand(AdminUserId, Guid.CreateVersion7(), Roles.Admin));
        var host = new CheckOutBookingCommandValidator().Validate(new CheckOutBookingCommand(HostUserId, Guid.CreateVersion7()));

        // Assert
        admin.Errors.Should().ContainSingle(e => e.ErrorMessage == BookingsErrors.AdminReasonRequired.Message);
        host.IsValid.Should().BeTrue();
    }

    private CancelBookingsForSuspensionCommandHandler Suspension(Mock<IBookingRepository> repository) =>
        new(repository.Object, _unitOfWork.Object, _payments.Object, ResponseBuilder(), Notifications(_notifier.Object), _auditTrail.Object, _time);
}
