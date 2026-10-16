using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings.DeactivateListing;
using PetHost.Modules.Listings.Application.Listings.ReactivateListing;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Audit;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Listings.Application.UnitTests.Listings;

public sealed class DeactivateListingCommandHandlerTests
{
    private static readonly DateTimeOffset Later = ListingBuilder.DefaultNow.AddDays(3);

    private readonly Mock<IListingRepository> _repository = new();
    private readonly Mock<IListingsUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Mock<IListingBookings> _bookings = new();
    private readonly Listing _listing = ListingsTestData.IndividualListing();
    private readonly DeactivateListingCommandHandler _sut;

    public DeactivateListingCommandHandlerTests()
    {
        _repository.Setup(r => r.GetByIdAsync(_listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_listing);

        _sut = new DeactivateListingCommandHandler(
            _repository.Object, _unitOfWork.Object, ListingsTestData.Hosts().Object, _bookings.Object, _auditTrail.Object, new FakeTimeProvider(Later));
    }

    [Fact]
    public async Task HandleAsync_Should_RefuseWithoutSaving_When_TheListingHasAnOpenBooking()
    {
        // Arrange
        _bookings.Setup(b => b.HasOpenBookingAsync(_listing.Id.Value, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var result = await _sut.HandleAsync(new DeactivateListingCommand(ListingsTestData.IndividualUserId, _listing.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.HasOpenBooking);
        _listing.IsActive.Should().BeTrue();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_NotAskTheBookings_When_TheListingIsAlreadyInactive()
    {
        // Arrange
        _listing.Deactivate(ListingBuilder.DefaultNow);

        // Act
        var result = await _sut.HandleAsync(new DeactivateListingCommand(ListingsTestData.IndividualUserId, _listing.Id.Value), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _bookings.Verify(b => b.HasOpenBookingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_DeactivateAndRecord_When_ListingIsActive()
    {
        // Act
        var result = await _sut.HandleAsync(new DeactivateListingCommand(ListingsTestData.IndividualUserId, _listing.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.IsActive.Should().BeFalse();
        result.Value.DeactivatedAt.Should().Be(Later);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.ListingDeactivated), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_NotSaveAgain_When_ListingIsAlreadyInactive()
    {
        // Arrange
        _listing.Deactivate(ListingBuilder.DefaultNow);

        // Act
        await _sut.HandleAsync(new DeactivateListingCommand(ListingsTestData.IndividualUserId, _listing.Id.Value), CancellationToken.None);

        // Assert
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_ListingIsFromAnotherHost()
    {
        // Act
        var result = await _sut.HandleAsync(new DeactivateListingCommand(ListingsTestData.CompanyUserId, _listing.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.NotFound(_listing.Id));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_ListingDoesNotExist()
    {
        // Act
        var unknown = Guid.CreateVersion7();
        var result = await _sut.HandleAsync(new DeactivateListingCommand(ListingsTestData.IndividualUserId, unknown), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.NotFound(new ListingId(unknown)));
    }
}

public sealed class ReactivateListingCommandHandlerTests
{
    private static readonly DateTimeOffset Later = ListingBuilder.DefaultNow.AddDays(3);

    private readonly Mock<IListingRepository> _repository = new();
    private readonly Mock<IListingsUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly ReactivateListingCommandHandler _sut;

    public ReactivateListingCommandHandlerTests() =>
        _sut = new ReactivateListingCommandHandler(
            _repository.Object, _unitOfWork.Object, ListingsTestData.Hosts().Object, _auditTrail.Object, new FakeTimeProvider(Later));

    [Fact]
    public async Task HandleAsync_Should_ReactivateAndRecord_When_ListingIsInactive()
    {
        // Arrange
        var listing = Inactive(ListingsTestData.IndividualListing());

        // Act
        var result = await _sut.HandleAsync(new ReactivateListingCommand(ListingsTestData.IndividualUserId, listing.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.IsActive.Should().BeTrue();
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.ListingReactivated), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnHostDeactivated_When_HostIsSuspended()
    {
        // Arrange
        var listing = Inactive(new ListingBuilder().OwnedBy(ListingsTestData.SuspendedHostId).Build());

        // Act
        var result = await _sut.HandleAsync(new ReactivateListingCommand(ListingsTestData.SuspendedUserId, listing.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.HostDeactivated);
        listing.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_Should_NotSave_When_ListingIsAlreadyActive()
    {
        // Arrange
        var listing = ListingsTestData.IndividualListing();
        _repository.Setup(r => r.GetByIdAsync(listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(listing);

        // Act
        await _sut.HandleAsync(new ReactivateListingCommand(ListingsTestData.IndividualUserId, listing.Id.Value), CancellationToken.None);

        // Assert
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private Listing Inactive(Listing listing)
    {
        listing.Deactivate(ListingBuilder.DefaultNow);
        _repository.Setup(r => r.GetByIdAsync(listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(listing);
        return listing;
    }
}
