using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings.CheckPetCompatibility;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Listings.Application.UnitTests.Listings;

/// <summary>
/// A Camila (tutora) vê o cantinho da Dona Cida (na casa tem cão, sem gato) com a Pipoca, que não
/// se dá com gatos. No período, a Dona Cida recebe a Mimi (gata de outro tutor).
/// </summary>
public sealed class CheckPetCompatibilityQueryHandlerTests
{
    private const string CheckIn = "2026-12-20";
    private const string CheckOut = "2026-12-25";

    private static readonly Guid CamilaOwnerId = Guid.CreateVersion7();
    private static readonly Guid OtherOwnerId = Guid.CreateVersion7();

    private readonly Listing _listing = ListingsTestData.IndividualListing();
    private readonly Mock<IListingRepository> _repository = new();
    private readonly Mock<IListingPets> _pets = new();
    private readonly Mock<IListingBookings> _bookings = ListingsTestData.Bookings();
    private readonly CompatibilityPet _pipoca = new(Guid.CreateVersion7(), "Pipoca", "dog", "medium", GoodWithDogs: true, GoodWithCats: false, GoodWithKids: true);
    private readonly CompatibilityPet _mimi = new(Guid.CreateVersion7(), "Mimi", "cat", "small", GoodWithDogs: true, GoodWithCats: true, GoodWithKids: true);
    private readonly CompatibilityPet _bidu = new(Guid.CreateVersion7(), "Bidu", "dog", "small", GoodWithDogs: true, GoodWithCats: true, GoodWithKids: true);
    private readonly CheckPetCompatibilityQueryHandler _sut;

    public CheckPetCompatibilityQueryHandlerTests()
    {
        _repository.Setup(r => r.GetByIdAsync(_listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_listing);

        _pets.Setup(p => p.FindOwnerIdAsync(ListingsTestData.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(CamilaOwnerId);
        _pets
            .Setup(p => p.GetByIdsAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(_pipoca.Id)), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ListingPet(CamilaOwnerId, IsActive: true, _pipoca)]);
        _pets
            .Setup(p => p.GetByIdsAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(_mimi.Id)), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ListingPet(OtherOwnerId, IsActive: true, _mimi)]);

        _bookings
            .Setup(b => b.GetGuestsAsync(_listing.Id.Value, new DateOnly(2026, 12, 20), new DateOnly(2026, 12, 25), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ListingGuest(OtherOwnerId, _mimi.Id), new ListingGuest(CamilaOwnerId, _bidu.Id)]);

        _sut = new CheckPetCompatibilityQueryHandler(
            _repository.Object,
            ListingsTestData.Hosts().Object,
            _pets.Object,
            _bookings.Object,
            new FakeTimeProvider(ListingBuilder.DefaultNow));
    }

    [Fact]
    public async Task HandleAsync_Should_MatchWithoutGuests_When_NoDatesAreSent()
    {
        // Act
        var result = await _sut.HandleAsync(Query(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Matches.Should().BeTrue();
        result.Value.PetName.Should().Be("Pipoca");
        result.Value.Warnings.Should().BeEmpty();
        result.Value.Guests.Should().BeNull();
        _bookings.Verify(b => b.GetGuestsAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_WarnAboutTheCatGuestAndSkipTheOwnersOwnPets_When_DatesAreSent()
    {
        // Act
        var result = await _sut.HandleAsync(Query(CheckIn, CheckOut), CancellationToken.None);

        // Assert
        result.Value!.Matches.Should().BeFalse();
        result.Value.Guests.Should().Equal(new CompatibilityGuestResponse("Mimi", "cat", "small"));
        result.Value.Warnings.Should().Equal(
            new CompatibilityWarningResponse(PetCompatibility.GuestIsCat, new CompatibilityGuestResponse("Mimi", "cat", "small")));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnAnEmptyGuestList_When_OnlyTheOwnersOwnPetsStayInThePeriod()
    {
        // Arrange
        _bookings
            .Setup(b => b.GetGuestsAsync(It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ListingGuest(CamilaOwnerId, _bidu.Id)]);

        // Act
        var result = await _sut.HandleAsync(Query(CheckIn, CheckOut), CancellationToken.None);

        // Assert
        result.Value!.Guests.Should().BeEmpty();
        result.Value.Matches.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnEveryValidationError_When_PetIsMissingAndDatesAreInvalid()
    {
        // Act
        var result = await _sut.HandleAsync(new CheckPetCompatibilityQuery(ListingsTestData.OwnerUserId, _listing.Id.Value, null, "2026-01-01", "2026-01-02"), CancellationToken.None);

        // Assert
        result.Errors.Should().BeEquivalentTo([ListingsErrors.CompatibilityPetRequired, ListingsErrors.CheckInFilterInThePast]);
        _repository.Verify(r => r.GetByIdAsync(It.IsAny<ListingId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_ListingDoesNotExist()
    {
        // Arrange
        var missing = new ListingId(Guid.CreateVersion7());

        // Act
        var result = await _sut.HandleAsync(new CheckPetCompatibilityQuery(ListingsTestData.OwnerUserId, missing.Value, _pipoca.Id), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.NotFound(missing));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_ListingIsInactive()
    {
        // Arrange
        _listing.Deactivate(ListingBuilder.DefaultNow);

        // Act
        var result = await _sut.HandleAsync(Query(), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.NotFound(_listing.Id));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_HostIsSuspended()
    {
        // Arrange
        var listing = new ListingBuilder().OwnedBy(ListingsTestData.SuspendedHostId).Build();
        _repository.Setup(r => r.GetByIdAsync(listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(listing);

        // Act
        var result = await _sut.HandleAsync(new CheckPetCompatibilityQuery(ListingsTestData.OwnerUserId, listing.Id.Value, _pipoca.Id), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.NotFound(listing.Id));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnOwnerNotFound_When_AccountHasNoOwnerProfile()
    {
        // Arrange
        var stranger = Guid.CreateVersion7();
        _pets.Setup(p => p.FindOwnerIdAsync(stranger, It.IsAny<CancellationToken>())).ReturnsAsync((Guid?)null);

        // Act
        var result = await _sut.HandleAsync(new CheckPetCompatibilityQuery(stranger, _listing.Id.Value, _pipoca.Id), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.OwnerNotFound);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnPetNotFound_When_PetIsFromAnotherOwner()
    {
        // Act
        var result = await _sut.HandleAsync(new CheckPetCompatibilityQuery(ListingsTestData.OwnerUserId, _listing.Id.Value, _mimi.Id), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.PetNotFound(_mimi.Id));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnPetNotFound_When_PetIsInactive()
    {
        // Arrange
        _pets
            .Setup(p => p.GetByIdsAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(_pipoca.Id)), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ListingPet(CamilaOwnerId, IsActive: false, _pipoca)]);

        // Act
        var result = await _sut.HandleAsync(Query(), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.PetNotFound(_pipoca.Id));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnPetNotFound_When_PetDoesNotExist()
    {
        // Arrange
        var missing = Guid.CreateVersion7();
        _pets
            .Setup(p => p.GetByIdsAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(missing)), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _sut.HandleAsync(new CheckPetCompatibilityQuery(ListingsTestData.OwnerUserId, _listing.Id.Value, missing), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.PetNotFound(missing));
    }

    private CheckPetCompatibilityQuery Query(string? checkIn = null, string? checkOut = null) =>
        new(ListingsTestData.OwnerUserId, _listing.Id.Value, _pipoca.Id, checkIn, checkOut);
}
