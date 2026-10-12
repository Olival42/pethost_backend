using FluentAssertions;
using Moq;
using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings.GetListingById;
using PetHost.Modules.Listings.Application.Listings.ListMyListings;
using PetHost.Modules.Listings.Application.Listings.SearchListings;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Authorization;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Listings.Application.UnitTests.Listings;

public sealed class GetListingByIdQueryHandlerTests
{
    private readonly Mock<IListingRepository> _repository = new();
    private readonly Listing _listing = ListingsTestData.IndividualListing();
    private readonly GetListingByIdQueryHandler _sut;

    public GetListingByIdQueryHandlerTests()
    {
        _repository.Setup(r => r.GetByIdAsync(_listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_listing);
        _sut = new GetListingByIdQueryHandler(_repository.Object, ListingsTestData.Hosts().Object);
    }

    [Fact]
    public async Task HandleAsync_Should_HideStreetAndLocation_When_ViewerIsNotTheOwner()
    {
        // Act
        var result = await _sut.HandleAsync(new GetListingByIdQuery(ListingsTestData.OwnerUserId, Roles.Owner, _listing.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.Address.Neighborhood.Should().Be("Zona 7");
        result.Value.Address.Street.Should().BeNull();
        result.Value.Address.Number.Should().BeNull();
        result.Value.Address.ZipCode.Should().BeNull();
        result.Value.Location.Should().BeNull();
        result.Value.Host!.Name.Should().Be("Dona Cida");
    }

    [Fact]
    public async Task HandleAsync_Should_ShowEverything_When_ViewerIsTheOwner()
    {
        // Act
        var result = await _sut.HandleAsync(new GetListingByIdQuery(ListingsTestData.IndividualUserId, Roles.Host, _listing.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.Address.Street.Should().Be("Rua das Flores");
        result.Value.Location.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_ListingIsInactiveAndViewerIsNotTheOwner()
    {
        // Arrange
        _listing.Deactivate(ListingBuilder.DefaultNow);

        // Act
        var result = await _sut.HandleAsync(new GetListingByIdQuery(ListingsTestData.CompanyUserId, Roles.Host, _listing.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.NotFound(_listing.Id));
    }

    [Theory]
    [InlineData(Roles.Host)]
    [InlineData(Roles.Admin)]
    public async Task HandleAsync_Should_ShowTheInactiveListing_When_ViewerIsTheOwnerOrAdmin(string role)
    {
        // Arrange
        _listing.Deactivate(ListingBuilder.DefaultNow);
        var userId = role == Roles.Host ? ListingsTestData.IndividualUserId : Guid.CreateVersion7();

        // Act
        var result = await _sut.HandleAsync(new GetListingByIdQuery(userId, role, _listing.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_HostIsSuspended()
    {
        // Arrange
        var listing = new ListingBuilder().OwnedBy(ListingsTestData.SuspendedHostId).Build();
        _repository.Setup(r => r.GetByIdAsync(listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(listing);

        // Act
        var result = await _sut.HandleAsync(new GetListingByIdQuery(ListingsTestData.OwnerUserId, Roles.Owner, listing.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.NotFound(listing.Id));
    }
}

public sealed class ListMyListingsQueryHandlerTests
{
    private readonly Mock<IListingRepository> _repository = new();

    [Fact]
    public async Task HandleAsync_Should_ReturnEveryBranchWithTheHostOnce_When_HostIsCompany()
    {
        // Arrange
        var branches = new[] { ListingsTestData.CompanyListing(), ListingsTestData.CompanyListing() };
        branches[1].Deactivate(ListingBuilder.DefaultNow);
        _repository.Setup(r => r.ListByHostAsync(ListingsTestData.CompanyHostId, It.IsAny<CancellationToken>())).ReturnsAsync(branches);
        var sut = new ListMyListingsQueryHandler(_repository.Object, ListingsTestData.Hosts().Object);

        // Act
        var result = await sut.HandleAsync(new ListMyListingsQuery(ListingsTestData.CompanyUserId), CancellationToken.None);

        // Assert
        result.Value!.Host!.Name.Should().Be("Hotel Pet Feliz");
        result.Value.Listings.Should().HaveCount(2);
        result.Value.Listings.Should().OnlyContain(l => l.Host == null && l.Address.Street != null);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnHostNotFound_When_AccountHasNoHostProfile()
    {
        // Arrange
        var sut = new ListMyListingsQueryHandler(_repository.Object, ListingsTestData.Hosts().Object);

        // Act
        var result = await sut.HandleAsync(new ListMyListingsQuery(ListingsTestData.OwnerUserId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.HostNotFound);
    }
}

public sealed class SearchListingsQueryHandlerTests
{
    private readonly Mock<IListingRepository> _repository = new();
    private readonly Mock<IAccountAddresses> _addresses = new();
    private readonly Mock<IGeocoder> _geocoder = ListingsTestData.Geocoder();
    private readonly SearchListingsQueryHandler _sut;
    private NearbySearch? _search;

    public SearchListingsQueryHandlerTests()
    {
        _repository
            .Setup(r => r.SearchNearbyAsync(It.IsAny<NearbySearch>(), It.IsAny<CancellationToken>()))
            .Callback<NearbySearch, CancellationToken>((search, _) => _search = search)
            .ReturnsAsync(([new NearbyListing(ListingsTestData.IndividualListing(), 2.345)], 1L));

        _addresses.Setup(a => a.GetAsync(ListingsTestData.OwnerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListingBuilder().BuildAddressData());

        _sut = new SearchListingsQueryHandler(_repository.Object, ListingsTestData.Hosts().Object, _addresses.Object, _geocoder.Object);
    }

    [Fact]
    public async Task HandleAsync_Should_SearchAroundTheAccountAddressAndHideUnavailableHosts()
    {
        // Act
        var result = await _sut.HandleAsync(new SearchListingsQuery(ListingsTestData.OwnerUserId, RadiusKm: 15), CancellationToken.None);

        // Assert
        _search!.Origin.Latitude.Should().Be(ListingBuilder.MaringaLatitude);
        _search.RadiusKm.Should().Be(15);
        _search.ExcludedHostIds.Should().Equal(ListingsTestData.SuspendedHostId);
        result.Value!.Items.Should().ContainSingle().Which.DistanceKm.Should().Be(2.3);
        result.Value.Items[0].Neighborhood.Should().Be("Zona 7");
        result.Value.TotalCount.Should().Be(1);
        _geocoder.Verify(g => g.LocateAsync(It.Is<ListingAddressData>(a => a.Neighborhood == "Zona 7"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_SearchAroundTheGivenPointWithoutTheMap_When_CoordinatesAreSent()
    {
        // Act
        var result = await _sut.HandleAsync(new SearchListingsQuery(ListingsTestData.OwnerUserId, -23.4m, -51.9m, 15), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _search!.Origin.Latitude.Should().Be(-23.4m);
        _search.Origin.Longitude.Should().Be(-51.9m);
        _addresses.Verify(a => a.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _geocoder.Verify(g => g.LocateAsync(It.IsAny<ListingAddressData>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnRangeErrors_When_CoordinatesAreOutOfRange()
    {
        // Act
        var result = await _sut.HandleAsync(new SearchListingsQuery(ListingsTestData.OwnerUserId, 91m, -181m), CancellationToken.None);

        // Assert
        result.Errors.Should().BeEquivalentTo([ListingsErrors.LatitudeOutOfRange, ListingsErrors.LongitudeOutOfRange]);
    }

    [Fact]
    public async Task HandleAsync_Should_UseTheDefaultRadius_When_NoneIsSent()
    {
        // Act
        var result = await _sut.HandleAsync(new SearchListingsQuery(ListingsTestData.OwnerUserId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _search!.RadiusKm.Should().Be(SearchListingsQuery.DefaultRadiusKm);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnAddressRequired_When_AccountHasNoAddress()
    {
        // Arrange
        _addresses.Setup(a => a.GetAsync(ListingsTestData.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync((ListingAddressData?)null);

        // Act
        var result = await _sut.HandleAsync(new SearchListingsQuery(ListingsTestData.OwnerUserId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.SearchAddressRequired);
        _repository.Verify(r => r.SearchNearbyAsync(It.IsAny<NearbySearch>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(GeocodingStatus.NotFound, "LISTING_SEARCH_ADDRESS_NOT_LOCATED")]
    [InlineData(GeocodingStatus.Unavailable, "LISTING_GEOCODER_UNAVAILABLE")]
    public async Task HandleAsync_Should_Fail_When_TheMapDoesNotLocateTheAccountAddress(GeocodingStatus status, string code)
    {
        // Arrange
        var geocoder = ListingsTestData.Geocoder(new GeocodingResult(status));
        var sut = new SearchListingsQueryHandler(_repository.Object, ListingsTestData.Hosts().Object, _addresses.Object, geocoder.Object);

        // Act
        var result = await sut.HandleAsync(new SearchListingsQuery(ListingsTestData.OwnerUserId), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be(code);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnEveryFilterError_When_QueryIsInvalid()
    {
        // Act
        var result = await _sut.HandleAsync(new SearchListingsQuery(ListingsTestData.OwnerUserId, Latitude: -23m, RadiusKm: 51, Page: 0, PageSize: 101), CancellationToken.None);

        // Assert
        result.Errors!.Select(e => e.Field).Should().BeEquivalentTo(["radiusKm", "latitude", "page", "pageSize"]);
        _repository.Verify(r => r.SearchNearbyAsync(It.IsAny<NearbySearch>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public async Task HandleAsync_Should_AcceptRadiusFrom5To50Km(int radiusKm, bool valid)
    {
        // Act
        var result = await _sut.HandleAsync(new SearchListingsQuery(ListingsTestData.OwnerUserId, RadiusKm: radiusKm), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().Be(valid);
    }
}
