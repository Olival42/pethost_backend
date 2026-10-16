using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
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
        _sut = new GetListingByIdQueryHandler(_repository.Object, ListingsTestData.Hosts().Object, ListingsTestData.Bookings().Object, new Mock<IAccountAddresses>().Object);
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

    [Fact]
    public async Task HandleAsync_Should_HideFromOthersButShowToTheOwner_When_StripeHasNotEnabledPayouts()
    {
        // Arrange: anfitrião aprovado pelo admin, mas o Stripe ainda não liberou os repasses.
        var hosts = ListingsTestData.Hosts();
        hosts
            .Setup(h => h.GetSummariesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ListingHostSummary>
            {
                [ListingsTestData.IndividualHostId] = new(ListingsTestData.IndividualHostId, "Dona Cida", null, IsCompany: false, IsAvailable: true, PayoutsEnabled: false),
            });
        var sut = new GetListingByIdQueryHandler(_repository.Object, hosts.Object, ListingsTestData.Bookings().Object, new Mock<IAccountAddresses>().Object);

        // Act
        var byOwner = await sut.HandleAsync(new GetListingByIdQuery(ListingsTestData.OwnerUserId, Roles.Owner, _listing.Id.Value), CancellationToken.None);
        var byHost = await sut.HandleAsync(new GetListingByIdQuery(ListingsTestData.IndividualUserId, Roles.Host, _listing.Id.Value), CancellationToken.None);

        // Assert
        byOwner.FirstError.Should().Be(ListingsErrors.NotFound(_listing.Id));
        byHost.Value!.Host!.PayoutsEnabled.Should().BeFalse("o anfitrião vê que falta o cadastro de recebimento");
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
    private ListingSearch? _search;

    public SearchListingsQueryHandlerTests()
    {
        _repository
            .Setup(r => r.SearchAsync(It.IsAny<ListingSearch>(), It.IsAny<CancellationToken>()))
            .Callback<ListingSearch, CancellationToken>((search, _) => _search = search)
            .ReturnsAsync(([new FoundListing(ListingsTestData.IndividualListing(), 2.345)], 1L));

        _addresses.Setup(a => a.GetAsync(ListingsTestData.OwnerUserId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListingBuilder().BuildAddressData());

        _sut = new SearchListingsQueryHandler(_repository.Object, ListingsTestData.Hosts().Object, _addresses.Object, _geocoder.Object, ListingsTestData.Bookings().Object, new FakeTimeProvider(ListingBuilder.DefaultNow));
    }

    [Fact]
    public async Task HandleAsync_Should_SearchAroundTheAccountAddressAndHideUnavailableHosts()
    {
        // Act
        var result = await _sut.HandleAsync(new SearchListingsQuery(ListingsTestData.OwnerUserId, RadiusKm: 15), CancellationToken.None);

        // Assert
        _search!.Area!.Origin.Latitude.Should().Be(ListingBuilder.MaringaLatitude);
        _search.Area.RadiusKm.Should().Be(15);
        _search.ExcludedHostIds.Should().Equal(ListingsTestData.SuspendedHostId);
        result.Value!.Items.Should().ContainSingle().Which.DistanceKm.Should().Be(2.3);
        result.Value.Items[0].Neighborhood.Should().Be("Zona 7");
        result.Value.TotalCount.Should().Be(1);
        _geocoder.Verify(g => g.LocateAsync(It.Is<ListingAddressData>(a => a.Neighborhood == "Zona 7"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(Roles.Host)]
    [InlineData(Roles.Owner)]
    public async Task HandleAsync_Should_AskTheAddressOfTheTokenRole(string role)
    {
        // Act: quem decide entre o endereço da empresa e o da conta é a porta, pelo papel do token.
        await _sut.HandleAsync(new SearchListingsQuery(ListingsTestData.OwnerUserId, Role: role), CancellationToken.None);

        // Assert
        _addresses.Verify(a => a.GetAsync(ListingsTestData.OwnerUserId, role, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_SearchAroundTheGivenPointWithoutTheMap_When_CoordinatesAreSent()
    {
        // Act
        var result = await _sut.HandleAsync(new SearchListingsQuery(ListingsTestData.OwnerUserId, -23.4m, -51.9m, 15), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _search!.Area!.Origin.Latitude.Should().Be(-23.4m);
        _search.Area.Origin.Longitude.Should().Be(-51.9m);
        _addresses.Verify(a => a.GetAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once, "o endereço só é lido para o selo Vizinho; o mapa não é consultado");
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
        _search!.Area!.RadiusKm.Should().Be(SearchListingsQuery.DefaultRadiusKm);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnAddressRequired_When_AccountHasNoAddress()
    {
        // Arrange
        _addresses.Setup(a => a.GetAsync(ListingsTestData.OwnerUserId, It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync((ListingAddressData?)null);

        // Act
        var result = await _sut.HandleAsync(new SearchListingsQuery(ListingsTestData.OwnerUserId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.SearchAddressRequired);
        _repository.Verify(r => r.SearchAsync(It.IsAny<ListingSearch>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(GeocodingStatus.NotFound, "LISTING_SEARCH_ADDRESS_NOT_LOCATED")]
    [InlineData(GeocodingStatus.Unavailable, "LISTING_GEOCODER_UNAVAILABLE")]
    public async Task HandleAsync_Should_Fail_When_TheMapDoesNotLocateTheAccountAddress(GeocodingStatus status, string code)
    {
        // Arrange
        var geocoder = ListingsTestData.Geocoder(new GeocodingResult(status));
        var sut = new SearchListingsQueryHandler(_repository.Object, ListingsTestData.Hosts().Object, _addresses.Object, geocoder.Object, ListingsTestData.Bookings().Object, new FakeTimeProvider(ListingBuilder.DefaultNow));

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
        _repository.Verify(r => r.SearchAsync(It.IsAny<ListingSearch>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_SearchWithoutRadiusOrMap_When_OnlyPlaceFiltersAreSent()
    {
        // Arrange
        _repository
            .Setup(r => r.SearchAsync(It.IsAny<ListingSearch>(), It.IsAny<CancellationToken>()))
            .Callback<ListingSearch, CancellationToken>((search, _) => _search = search)
            .ReturnsAsync(([new FoundListing(ListingsTestData.IndividualListing(), null)], 1L));

        // Act
        var result = await _sut.HandleAsync(
            new SearchListingsQuery(ListingsTestData.OwnerUserId, State: " pr ", City: " Maringá ", Neighborhood: "Zona 7"),
            CancellationToken.None);

        // Assert
        _search!.Area.Should().BeNull();
        _search.Filters.Should().Be(new ListingFilters(State: "PR", City: "Maringá", Neighborhood: "Zona 7"));
        result.Value!.Items.Should().ContainSingle().Which.DistanceKm.Should().BeNull();
        _addresses.Verify(a => a.GetAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once, "o endereço só é lido para o selo Vizinho; o mapa não é consultado");
        _geocoder.Verify(g => g.LocateAsync(It.IsAny<ListingAddressData>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_CombinePlaceFiltersWithTheRadius_When_RadiusIsSent()
    {
        // Act
        await _sut.HandleAsync(new SearchListingsQuery(ListingsTestData.OwnerUserId, RadiusKm: 20, City: "Maringá"), CancellationToken.None);

        // Assert
        _search!.Area!.RadiusKm.Should().Be(20);
        _search.Filters.City.Should().Be("Maringá");
    }

    [Fact]
    public async Task HandleAsync_Should_JoinEveryFilter_When_ManyAreSent()
    {
        // Act
        await _sut.HandleAsync(
            new SearchListingsQuery(
                ListingsTestData.OwnerUserId,
                Species: ["dog", "CAT"],
                Sizes: ["small,large"],
                Weekdays: ["saturday", "sunday"],
                MinNightlyRate: 30m,
                MaxNightlyRate: 80m,
                MinCapacity: 2,
                HasYard: true,
                HomeType: " Apartment ",
                HasDogs: false,
                HasCats: true,
                HasKids: false),
            CancellationToken.None);

        // Assert
        _search!.Filters.Should().Be(new ListingFilters(
            Species: AcceptedSpecies.Dog | AcceptedSpecies.Cat,
            AcceptsSmall: true,
            AcceptsLarge: true,
            Weekdays: Weekdays.Saturday | Weekdays.Sunday,
            MinNightlyRate: 30m,
            MaxNightlyRate: 80m,
            MinCapacity: 2,
            HasYard: true,
            HomeType: HomeType.Apartment,
            HasDogs: false,
            HasCats: true,
            HasKids: false));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnEveryInvalidFilter_When_FiltersAreUnknownOrOutOfRange()
    {
        // Act
        var result = await _sut.HandleAsync(
            new SearchListingsQuery(
                ListingsTestData.OwnerUserId,
                State: "XX",
                City: new string('c', 81),
                Neighborhood: new string('n', 81),
                Species: ["dragon"],
                Sizes: ["huge"],
                Weekdays: ["someday"],
                MinNightlyRate: 90m,
                MaxNightlyRate: 50m,
                MinCapacity: 0,
                HomeType: "castle"),
            CancellationToken.None);

        // Assert
        result.Errors!.Select(e => e.Field).Should().BeEquivalentTo(
            ["state", "city", "neighborhood", "species", "sizes", "weekdays", "maxNightlyRate", "minCapacity", "homeType"]);
        _repository.Verify(r => r.SearchAsync(It.IsAny<ListingSearch>(), It.IsAny<CancellationToken>()), Times.Never);
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
