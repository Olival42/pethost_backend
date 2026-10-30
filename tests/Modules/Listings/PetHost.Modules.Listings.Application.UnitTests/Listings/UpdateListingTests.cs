using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings.UpdateListing;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Audit;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Listings.Application.UnitTests.Listings;

public sealed class UpdateListingCommandHandlerTests
{
    private static readonly DateTimeOffset Later = ListingBuilder.DefaultNow.AddDays(1);

    private readonly Mock<IListingRepository> _repository = new();
    private readonly Mock<IListingsUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Mock<IGeocoder> _geocoder = ListingsTestData.Geocoder(GeocodingResult.Found(-23.41m, -51.92m));
    private readonly Listing _listing = ListingsTestData.IndividualListing();
    private readonly UpdateListingCommandHandler _sut;

    public UpdateListingCommandHandlerTests()
    {
        _repository.Setup(r => r.GetByIdAsync(_listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_listing);

        _sut = new UpdateListingCommandHandler(
            _repository.Object, _unitOfWork.Object, ListingsTestData.Hosts().Object, _geocoder.Object, _auditTrail.Object, new FakeTimeProvider(Later));
    }

    [Fact]
    public async Task HandleAsync_Should_ChangeOnlyTheRateAndRecordTheField_When_OnlyTheRateIsSent()
    {
        // Act
        var result = await _sut.HandleAsync(Patch() with { NightlyRate = 75.5m }, CancellationToken.None);

        // Assert
        result.Value!.NightlyRate.Should().Be(75.5m);
        result.Value.Title.Should().Be("Casa com quintal na Zona 7");
        _geocoder.Verify(g => g.LocateAsync(It.IsAny<ListingAddressData>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditTrail.Verify(
            a => a.RecordAsync(It.Is<AuditRecord>(r => r.Details!["fields"] == "nightlyRate"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_GeocodeAgain_When_TheAddressChanges()
    {
        // Act
        var result = await _sut.HandleAsync(
            Patch() with { Address = new(null, null, "300", null, "Zona 5", null, null) },
            CancellationToken.None);

        // Assert
        result.Value!.Address.Neighborhood.Should().Be("Zona 5");
        result.Value.Address.Street.Should().Be("Rua das Flores", "campo ausente no endereço mantém o atual");
        result.Value.Location!.Latitude.Should().Be(-23.41m);
        _auditTrail.Verify(
            a => a.RecordAsync(It.Is<AuditRecord>(r => r.Details!["fields"] == "address.number,address.neighborhood"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_NotSave_When_NothingChanged()
    {
        // Act
        var result = await _sut.HandleAsync(Patch() with { Title = "Casa com quintal na Zona 7" }, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReplaceTheWeekdays_When_AListIsSent()
    {
        // Act
        var result = await _sut.HandleAsync(Patch() with { AvailableWeekdays = ["saturday", "sunday"] }, CancellationToken.None);

        // Assert
        result.Value!.AvailableWeekdays.Should().Equal("saturday", "sunday");
    }

    [Fact]
    public async Task HandleAsync_Should_ValidateTheMergedResult_When_PatchBreaksARule()
    {
        // Act
        var result = await _sut.HandleAsync(Patch() with { AcceptsSmall = false, AcceptsMedium = false }, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.SizeRequiredForDogsAndCats);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_ListingIsFromAnotherHost()
    {
        // Act
        var result = await _sut.HandleAsync(Patch(ListingsTestData.CompanyUserId) with { NightlyRate = 70m }, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.NotFound(_listing.Id));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnAddressNotLocated_When_TheNewAddressIsNotOnTheMap()
    {
        // Arrange
        var geocoder = ListingsTestData.Geocoder(GeocodingResult.NotFound);
        var sut = new UpdateListingCommandHandler(
            _repository.Object, _unitOfWork.Object, ListingsTestData.Hosts().Object, geocoder.Object, _auditTrail.Object, new FakeTimeProvider(Later));

        // Act
        var result = await sut.HandleAsync(Patch() with { Address = new(null, "Rua Inexistente", null, null, null, null, null) }, CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.AddressNotLocated);
    }

    private UpdateListingCommand Patch(Guid? userId = null) =>
        new(userId ?? ListingsTestData.IndividualUserId, _listing.Id.Value, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
}
