using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.ExpireOverdueBookings;
using PetHost.Modules.Bookings.Application.Bookings.GetBookingById;
using PetHost.Modules.Bookings.Application.Bookings.ListMyBookings;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Bookings.Application.UnitTests.Bookings;

public sealed class GetBookingByIdQueryHandlerTests
{
    private readonly Booking _booking = new BookingBuilder().Build();
    private readonly GetBookingByIdQueryHandler _sut;

    public GetBookingByIdQueryHandlerTests() =>
        _sut = new GetBookingByIdQueryHandler(
            BookingsTestData.Repository(_booking).Object,
            BookingsTestData.Parties().Object,
            BookingsTestData.ResponseBuilder());

    [Theory]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Host)]
    public async Task HandleAsync_Should_ReturnTheBooking_When_CallerIsAParty(string role)
    {
        // Arrange
        var userId = role == Roles.Owner ? BookingsTestData.OwnerUserId : BookingsTestData.HostUserId;

        // Act
        var result = await _sut.HandleAsync(new GetBookingByIdQuery(userId, role, _booking.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.Id.Should().Be(_booking.Id.Value);
        result.Value.Owner!.Name.Should().Be("Camila Souza");
        result.Value.Host!.Name.Should().Be("Dona Cida");
    }

    [Theory]
    [InlineData(Roles.Owner)]
    [InlineData(Roles.Host)]
    public async Task HandleAsync_Should_ReturnNotFound_When_CallerIsAStranger(string role)
    {
        // Arrange
        var userId = role == Roles.Owner ? BookingsTestData.OtherOwnerUserId : BookingsTestData.OtherHostUserId;

        // Act
        var result = await _sut.HandleAsync(new GetBookingByIdQuery(userId, role, _booking.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.NotFound(_booking.Id));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnAnyBooking_When_CallerIsAdmin()
    {
        // Act
        var result = await _sut.HandleAsync(new GetBookingByIdQuery(Guid.CreateVersion7(), Roles.Admin, _booking.Id.Value), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_AdminAsksForUnknownId()
    {
        // Arrange
        var unknown = Guid.CreateVersion7();

        // Act
        var result = await _sut.HandleAsync(new GetBookingByIdQuery(Guid.CreateVersion7(), Roles.Admin, unknown), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.NotFound(new BookingId(unknown)));
    }

    [Fact]
    public async Task HandleAsync_Should_FillMissingPetWithItsIdOnly_When_PetVanished()
    {
        // Arrange
        var sut = new GetBookingByIdQueryHandler(
            BookingsTestData.Repository(_booking).Object,
            BookingsTestData.Parties().Object,
            new BookingResponseBuilder(BookingsTestData.Listings().Object, BookingsTestData.Parties().Object, BookingsTestData.Pets(BookingBuilder.Thor()).Object, BookingsTestData.Reviews().Object));

        // Act
        var result = await sut.HandleAsync(new GetBookingByIdQuery(BookingsTestData.OwnerUserId, Roles.Owner, _booking.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.Pets.Should().ContainSingle().Which.Should().Be(new BookingPetResponse(BookingBuilder.PipocaId, null, null, null));
    }
}

public sealed class ListMyBookingsQueryHandlerTests
{
    private readonly Mock<IBookingRepository> _repository = BookingsTestData.Repository();
    private readonly ListMyBookingsQueryHandler _sut;
    private BookingSearch? _search;

    public ListMyBookingsQueryHandlerTests()
    {
        var bookings = new List<Booking> { new BookingBuilder().Build() };

        _repository
            .Setup(r => r.SearchAsync(It.IsAny<BookingSearch>(), It.IsAny<CancellationToken>()))
            .Callback((BookingSearch search, CancellationToken _) => _search = search)
            .ReturnsAsync((bookings, 21L));

        _sut = new ListMyBookingsQueryHandler(_repository.Object, BookingsTestData.Parties().Object, BookingsTestData.ResponseBuilder());
    }

    [Fact]
    public async Task HandleAsync_Should_SearchTheOwnersBookings_When_CallerIsAnOwner()
    {
        // Act
        var result = await _sut.HandleAsync(new ListMyBookingsQuery(BookingsTestData.OwnerUserId, Roles.Owner, ["requested,confirmed"], 2, 10), CancellationToken.None);

        // Assert
        result.Value!.Items.Should().ContainSingle();
        result.Value.TotalCount.Should().Be(21);
        result.Value.TotalPages.Should().Be(3);
        _search.Should().BeEquivalentTo(BookingSearch.ForOwner(BookingBuilder.OwnerId, [BookingStatus.Requested, BookingStatus.Confirmed], 2, 10));
    }

    [Fact]
    public async Task HandleAsync_Should_SearchTheHostsBookings_When_CallerIsAHost()
    {
        // Act
        await _sut.HandleAsync(new ListMyBookingsQuery(BookingsTestData.HostUserId, Roles.Host), CancellationToken.None);

        // Assert
        _search.Should().BeEquivalentTo(BookingSearch.ForHost(BookingBuilder.HostId, [], 1, 20));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnEveryValidationError_When_FiltersAreInvalid()
    {
        // Act
        var result = await _sut.HandleAsync(new ListMyBookingsQuery(BookingsTestData.OwnerUserId, Roles.Owner, ["paid"], 0, 101), CancellationToken.None);

        // Assert
        result.Errors.Should().BeEquivalentTo([BookingsErrors.StatusFilterInvalid, BookingsErrors.PageInvalid, BookingsErrors.PageSizeInvalid]);
        _repository.Verify(r => r.SearchAsync(It.IsAny<BookingSearch>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnHostNotFound_When_HostAccountIsNotVerified()
    {
        // Act
        var result = await _sut.HandleAsync(new ListMyBookingsQuery(BookingsTestData.NoProfileUserId, Roles.Host), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.HostNotFound);
    }
}

public sealed class ExpireOverdueBookingsCommandHandlerTests
{
    private static readonly DateTimeOffset Later = BookingBuilder.DefaultNow.AddHours(30);

    private readonly Mock<IBookingRepository> _repository = BookingsTestData.Repository();
    private readonly Mock<IBookingsUnitOfWork> _unitOfWork = BookingsTestData.UnitOfWork();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Mock<IBookingPayments> _payments = BookingsTestData.Payments();
    private readonly ExpireOverdueBookingsCommandHandler _sut;

    public ExpireOverdueBookingsCommandHandlerTests() =>
        _sut = new ExpireOverdueBookingsCommandHandler(_repository.Object, _unitOfWork.Object, _payments.Object, _auditTrail.Object, new FakeTimeProvider(Later));

    [Fact]
    public async Task HandleAsync_Should_ExpireOverdueRequestsAndRecordEach()
    {
        // Arrange
        var first = new BookingBuilder().Build();
        var second = new BookingBuilder().WithPets(BookingBuilder.Thor()).Build();
        _repository
            .Setup(r => r.ListOverdueAsync(Later - Booking.ResponseWindow, Booking.LastCheckInClosedToAcceptance(Later), Later - Booking.PaymentWindow, BookingClock.Today(Later), ExpireOverdueBookingsCommand.BatchSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync([first, second]);

        // Act
        var result = await _sut.HandleAsync(new ExpireOverdueBookingsCommand(), CancellationToken.None);

        // Assert
        result.Value!.Expired.Should().Be(2);
        first.Status.Should().Be(BookingStatus.Expired);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.BookingExpired), It.IsAny<CancellationToken>()), Times.Exactly(2));
        _payments.Verify(p => p.BookingChangedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ExpireUnpaidAcceptedBookingAndNotifyPayments_When_PaymentWindowIsOver()
    {
        // Arrange
        var accepted = new BookingBuilder().BuildAccepted();
        _repository
            .Setup(r => r.ListOverdueAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateOnly>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([accepted]);

        // Act
        var result = await _sut.HandleAsync(new ExpireOverdueBookingsCommand(), CancellationToken.None);

        // Assert
        result.Value!.Expired.Should().Be(1);
        accepted.Status.Should().Be(BookingStatus.Expired);
        _payments.Verify(p => p.BookingChangedAsync(accepted.Id.Value, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_NotSave_When_NothingIsOverdue()
    {
        // Arrange
        _repository
            .Setup(r => r.ListOverdueAsync(It.IsAny<DateTimeOffset>(), It.IsAny<DateOnly>(), It.IsAny<DateTimeOffset>(), It.IsAny<DateOnly>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _sut.HandleAsync(new ExpireOverdueBookingsCommand(), CancellationToken.None);

        // Assert
        result.Value!.Expired.Should().Be(0);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
