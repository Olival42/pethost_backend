using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings.CreateListing;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Audit;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Listings.Application.UnitTests.Listings;

public sealed class CreateListingCommandHandlerTests
{
    private readonly Mock<IListingRepository> _repository = new();
    private readonly Mock<IListingsUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private Mock<IGeocoder> _geocoder = ListingsTestData.Geocoder();

    private CreateListingCommandHandler Sut() =>
        new(_repository.Object, _unitOfWork.Object, ListingsTestData.Hosts().Object, _geocoder.Object, _auditTrail.Object, new FakeTimeProvider(ListingBuilder.DefaultNow));

    [Fact]
    public async Task HandleAsync_Should_CreateWithGeocodedLocation_When_IndividualHasNoListing()
    {
        // Arrange
        Listing? added = null;
        _repository.Setup(r => r.Add(It.IsAny<Listing>())).Callback<Listing>(l => added = l);

        // Act
        var result = await Sut().HandleAsync(Command(ListingsTestData.IndividualUserId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        added!.HostId.Should().Be(ListingsTestData.IndividualHostId);
        result.Value!.Location!.Latitude.Should().Be(ListingBuilder.MaringaLatitude);
        result.Value.Address.Street.Should().Be("Rua das Flores", "o dono vê o endereço completo");
        result.Value.NightlyRate.Should().Be(60m);
        result.Value.AvailableWeekdays.Should().Equal("monday", "friday");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.ListingCreated), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnLimitReachedWithoutCallingTheMap_When_IndividualAlreadyHasAListing()
    {
        // Arrange
        _repository.Setup(r => r.CountByHostAsync(ListingsTestData.IndividualHostId, It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // Act
        var result = await Sut().HandleAsync(Command(ListingsTestData.IndividualUserId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.LimitReached);
        _geocoder.Verify(g => g.LocateAsync(It.IsAny<ListingAddressData>(), It.IsAny<CancellationToken>()), Times.Never);
        _repository.Verify(r => r.Add(It.IsAny<Listing>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_CreateAnotherBranch_When_CompanyAlreadyHasListings()
    {
        // Arrange
        _repository.Setup(r => r.CountByHostAsync(ListingsTestData.CompanyHostId, It.IsAny<CancellationToken>())).ReturnsAsync(3);

        // Act
        var result = await Sut().HandleAsync(Command(ListingsTestData.CompanyUserId) with { HomeType = null, Capacity = 120 }, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.HomeType.Should().BeNull();
        result.Value.Capacity.Should().Be(120);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnDetailsAndAddressErrorsTogether_When_BodyIsInvalid()
    {
        // Act
        var result = await Sut().HandleAsync(
            Command(ListingsTestData.IndividualUserId) with { Capacity = 50, Address = ListingsTestData.Address() with { State = "XX" } },
            CancellationToken.None);

        // Assert
        result.Errors!.Select(e => e.Field).Should().BeEquivalentTo(["capacity", "address.state"]);
        _geocoder.Verify(g => g.LocateAsync(It.IsAny<ListingAddressData>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnHostNotFound_When_AccountHasNoHostProfile()
    {
        // Act
        var result = await Sut().HandleAsync(Command(ListingsTestData.OwnerUserId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.HostNotFound);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnHostDeactivated_When_HostIsSuspended()
    {
        // Act
        var result = await Sut().HandleAsync(Command(ListingsTestData.SuspendedUserId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.HostDeactivated);
    }

    [Fact]
    public async Task HandleAsync_Should_RefuseWithoutCallingTheMap_When_StripeHasNotApprovedThePayoutSetup()
    {
        // Arrange: aprovado pelo admin, mas o Stripe ainda não liberou os repasses.
        var hosts = ListingsTestData.Hosts();
        hosts.Setup(h => h.FindByAccountAsync(ListingsTestData.IndividualUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostProfile(ListingsTestData.IndividualHostId, IsCompany: false, IsAvailable: true, PayoutsEnabled: false));
        var sut = new CreateListingCommandHandler(
            _repository.Object, _unitOfWork.Object, hosts.Object, _geocoder.Object, _auditTrail.Object, new FakeTimeProvider(ListingBuilder.DefaultNow));

        // Act
        var result = await sut.HandleAsync(Command(ListingsTestData.IndividualUserId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.HostPayoutsNotEnabled);
        _geocoder.Verify(g => g.LocateAsync(It.IsAny<ListingAddressData>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(GeocodingStatus.NotFound, "LISTING_ADDRESS_NOT_LOCATED")]
    [InlineData(GeocodingStatus.Unavailable, "LISTING_GEOCODER_UNAVAILABLE")]
    public async Task HandleAsync_Should_NotSave_When_TheMapDoesNotLocateTheAddress(GeocodingStatus status, string code)
    {
        // Arrange
        _geocoder = ListingsTestData.Geocoder(new GeocodingResult(status));

        // Act
        var result = await Sut().HandleAsync(Command(ListingsTestData.IndividualUserId), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be(code);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CreateListingCommand Command(Guid userId) =>
        new(
            userId,
            "Casa com quintal na Zona 7",
            "Casa térrea com quintal.",
            "house",
            HasYard: true,
            HasDogs: true,
            HasCats: false,
            HasKids: false,
            AcceptedSpecies: ["dog"],
            AcceptsSmall: true,
            AcceptsMedium: true,
            AcceptsLarge: false,
            Capacity: 3,
            NightlyRate: 60m,
            HouseRules: null,
            AvailableWeekdays: ["friday", "monday"],
            Address: ListingsTestData.Address());
}
