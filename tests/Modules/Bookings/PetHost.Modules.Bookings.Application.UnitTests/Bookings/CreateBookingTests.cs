using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.CreateBooking;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Bookings.Application.UnitTests.Bookings;

public sealed class CreateBookingCommandHandlerTests
{
    private readonly Mock<IBookingRepository> _repository = BookingsTestData.Repository();
    private readonly Mock<IBookingsUnitOfWork> _unitOfWork = BookingsTestData.UnitOfWork();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private Mock<IBookingListings> _listings = BookingsTestData.Listings();

    private CreateBookingCommandHandler Sut() => new(
        _repository.Object,
        _unitOfWork.Object,
        BookingsTestData.Parties().Object,
        _listings.Object,
        BookingsTestData.Pets().Object,
        BookingsTestData.ResponseBuilder(),
        _auditTrail.Object,
        new FakeTimeProvider(BookingBuilder.DefaultNow));

    private static CreateBookingCommand Command(Guid? userId = null, Guid? listingId = null, params Guid[] petIds) => new(
        userId ?? BookingsTestData.OwnerUserId,
        listingId ?? BookingBuilder.ListingId,
        petIds.Length == 0 ? [BookingBuilder.PipocaId] : petIds,
        "2026-10-20",
        "2026-10-25");

    [Fact]
    public async Task HandleAsync_Should_SaveRequestedBookingAndRecord_When_EverythingIsValid()
    {
        // Act
        var result = await Sut().HandleAsync(Command(petIds: [BookingBuilder.PipocaId, BookingBuilder.ThorId]), CancellationToken.None);

        // Assert
        var booking = result.Value!;
        booking.Status.Should().Be("requested");
        booking.TotalPrice.Should().Be(600m);
        booking.Nights.Should().Be(5);
        booking.CheckIn.Should().Be(new DateOnly(2026, 10, 20));
        booking.ResponseDeadline.Should().Be(BookingBuilder.DefaultNow.AddHours(24));
        booking.Pets.Select(p => p.Name).Should().Equal("Pipoca", "Thor");
        booking.Listing!.Neighborhood.Should().Be("Zona 7");
        _repository.Verify(r => r.Add(It.Is<Booking>(b => b.PetCount == 2)), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.BookingRequested && r.TargetType == AuditTargets.Booking), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnOwnerNotFound_When_AccountHasNoOwnerProfile()
    {
        // Act
        var result = await Sut().HandleAsync(Command(BookingsTestData.NoProfileUserId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.OwnerNotFound);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnOwnerDeactivated_When_OwnerIsInactive()
    {
        // Act
        var result = await Sut().HandleAsync(Command(BookingsTestData.InactiveOwnerUserId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.OwnerDeactivated);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnListingNotFound_When_ListingDoesNotExist()
    {
        // Arrange
        var unknown = Guid.CreateVersion7();

        // Act
        var result = await Sut().HandleAsync(Command(listingId: unknown), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.ListingNotFound(unknown));
        _repository.Verify(r => r.Add(It.IsAny<Booking>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_NotSave_When_DomainRejectsTheRequest()
    {
        // Arrange
        _listings = BookingsTestData.Listings(BookingBuilder.Listing() with { AcceptedSpecies = ["cat"] });

        // Act
        var result = await Sut().HandleAsync(Command(), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.SpeciesNotAccepted("Pipoca", "dog"));
        _repository.Verify(r => r.Add(It.IsAny<Booking>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_PassTheBusyPetsToTheDomain()
    {
        // Arrange
        _repository
            .Setup(r => r.ListPetsBookedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<StayPeriod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([BookingBuilder.PipocaId]);

        // Act
        var result = await Sut().HandleAsync(Command(), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.PetPeriodConflict("Pipoca"));
    }

    [Fact]
    public async Task HandleAsync_Should_CountOnlyConfirmedStaysForCapacity()
    {
        // Arrange
        _listings = BookingsTestData.Listings(BookingBuilder.Listing() with { Capacity = 1 });
        var other = new BookingBuilder().WithPets(BookingBuilder.Thor()).BuildConfirmed();
        _repository
            .Setup(r => r.ListOccupyingAsync(BookingBuilder.ListingId, It.IsAny<StayPeriod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([other]);

        // Act
        var result = await Sut().HandleAsync(Command(), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.CapacityExceeded(BookingBuilder.DefaultCheckIn));
    }

    [Fact]
    public async Task HandleAsync_Should_NotRecord_When_SaveFails()
    {
        // Arrange
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(BookingsErrors.Conflict));

        // Act
        var result = await Sut().HandleAsync(Command(), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(BookingsErrors.Conflict);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

public sealed class CreateBookingCommandValidatorTests
{
    private readonly CreateBookingCommandValidator _sut = new(new FakeTimeProvider(BookingBuilder.DefaultNow));

    private static CreateBookingCommand Valid() =>
        new(Guid.CreateVersion7(), BookingBuilder.ListingId, [BookingBuilder.PipocaId], "2026-10-20", "2026-10-25");

    private List<string> Fields(CreateBookingCommand command) =>
        [.. _sut.Validate(command).Errors.Select(e => e.PropertyName)];

    private List<string> Messages(CreateBookingCommand command) =>
        [.. _sut.Validate(command).Errors.Select(e => e.ErrorMessage)];

    [Fact]
    public void Validate_Should_Pass_When_CommandIsValid()
    {
        // Act
        var result = _sut.Validate(Valid());

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_ReportEveryMissingField_When_CommandIsEmpty()
    {
        // Act
        var fields = Fields(new CreateBookingCommand(Guid.CreateVersion7(), Guid.Empty, null, null, null));

        // Assert
        fields.Should().BeEquivalentTo(["ListingId", "PetIds", "CheckIn", "CheckOut"]);
    }

    [Fact]
    public void Validate_Should_ReportFormat_When_DateIsNotIso()
    {
        // Act
        var messages = Messages(Valid() with { CheckIn = "20/10/2026" });

        // Assert
        messages.Should().ContainSingle().Which.Should().Be(BookingsErrors.CheckInInvalid.Message);
    }

    [Fact]
    public void Validate_Should_ApplyThePeriodRule_When_CheckInIsInThePast()
    {
        // Act
        var messages = Messages(Valid() with { CheckIn = "2026-10-10" });

        // Assert
        messages.Should().ContainSingle().Which.Should().Be(BookingsErrors.CheckInInThePast.Message);
    }

    [Fact]
    public void Validate_Should_ReportBothFields_When_CheckInIsPastAndCheckOutIsMalformed()
    {
        // Act
        var messages = Messages(Valid() with { CheckIn = "2026-10-10", CheckOut = "amanhã" });

        // Assert
        messages.Should().BeEquivalentTo([BookingsErrors.CheckInInThePast.Message, BookingsErrors.CheckOutInvalid.Message]);
    }

    [Fact]
    public void Validate_Should_ApplyThePeriodRule_When_CheckOutIsBeforeCheckIn()
    {
        // Act
        var result = _sut.Validate(Valid() with { CheckOut = "2026-10-19" });

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "checkOut" && e.ErrorMessage == BookingsErrors.CheckOutNotAfterCheckIn.Message);
    }

    [Fact]
    public void Validate_Should_RejectMoreThanTenPets()
    {
        // Act
        var messages = Messages(Valid() with { PetIds = [.. Enumerable.Range(0, Booking.MaxPets + 1).Select(_ => Guid.CreateVersion7())] });

        // Assert
        messages.Should().ContainSingle().Which.Should().Be(BookingsErrors.PetIdsTooMany.Message);
    }

    [Fact]
    public void Validate_Should_RejectRepeatedPets()
    {
        // Act
        var messages = Messages(Valid() with { PetIds = [BookingBuilder.PipocaId, BookingBuilder.PipocaId] });

        // Assert
        messages.Should().ContainSingle().Which.Should().Be(BookingsErrors.PetIdsInvalid.Message);
    }

    [Fact]
    public void Validate_Should_RejectEmptyPetId()
    {
        // Act
        var messages = Messages(Valid() with { PetIds = [Guid.Empty] });

        // Assert
        messages.Should().ContainSingle().Which.Should().Be(BookingsErrors.PetIdsInvalid.Message);
    }
}
