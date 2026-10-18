using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings.GetListingById;
using PetHost.Modules.Listings.Application.Listings.SearchListings;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Authorization;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Listings.Application.UnitTests.Listings;

/// <summary>Busca por datas e quantidade de pets, e os selos.</summary>
public sealed class ListingAvailabilityTests
{
    // ListingBuilder.DefaultNow = domingo, 11/10/2026. 20/10 é terça; 23/10, sexta.
    private const string Tuesday = "2026-10-20";
    private const string Friday = "2026-10-23";

    private readonly Mock<IListingRepository> _repository = new();
    private readonly Mock<IAccountAddresses> _addresses = new();
    private readonly Mock<IListingBookings> _bookings = ListingsTestData.Bookings();
    private readonly Listing _listing = ListingsTestData.IndividualListing();
    private ListingSearch? _search;

    public ListingAvailabilityTests()
    {
        _repository
            .Setup(r => r.SearchAsync(It.IsAny<ListingSearch>(), It.IsAny<CancellationToken>()))
            .Callback<ListingSearch, CancellationToken>((search, _) => _search = search)
            .ReturnsAsync(([new FoundListing(_listing, 1.2)], 1L));

        _repository.Setup(r => r.GetByIdAsync(_listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_listing);

        _addresses.Setup(a => a.GetAsync(ListingsTestData.OwnerUserId, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListingBuilder().BuildAddressData());
    }

    private SearchListingsQueryHandler Search() => new(
        _repository.Object,
        ListingsTestData.Hosts().Object,
        _addresses.Object,
        ListingsTestData.Geocoder().Object,
        _bookings.Object,
        ListingsTestData.Reviews().Object,
        new FakeTimeProvider(ListingBuilder.DefaultNow));

    private static SearchListingsQuery Query(string? checkIn = Tuesday, string? checkOut = Friday, int? pets = null) =>
        new(ListingsTestData.OwnerUserId, City: "Maringá", CheckIn: checkIn, CheckOut: checkOut, Pets: pets);

    [Fact]
    public async Task Search_Should_RequireEveryWeekdayOfTheStayAndRoomForThePets_When_DatesAreSent()
    {
        // Act
        await Search().HandleAsync(Query(pets: 2), CancellationToken.None);

        // Assert: de terça a sexta, inclusive.
        _search!.Filters.Weekdays.Should().Be(Weekdays.Tuesday | Weekdays.Wednesday | Weekdays.Thursday | Weekdays.Friday);
        _search.Filters.MinCapacity.Should().Be(2);
    }

    [Fact]
    public async Task Search_Should_ExcludeListingsWithoutRoom_When_ConfirmedStaysFillThem()
    {
        // Arrange: cantinho A (capacidade 3) já tem 2 pets numa noite; cantinho B (capacidade 3) tem 1.
        var full = ListingId.New();
        var roomy = ListingId.New();
        _bookings
            .Setup(b => b.GetPeakOccupancyAsync(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 23), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int> { [full.Value] = 2, [roomy.Value] = 1 });
        _repository
            .Setup(r => r.GetCapacitiesAsync(It.IsAny<IReadOnlyCollection<ListingId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<ListingId, int> { [full] = 3, [roomy] = 3 });

        // Act: 2 pets.
        await Search().HandleAsync(Query(pets: 2), CancellationToken.None);

        // Assert — as datas seguem para o repositório tirar os cantinhos com a agenda fechada.
        _search!.ExcludedListingIds.Should().Equal(full);
        _search.Stay.Should().Be(new StayWindow(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 23)));
    }

    [Fact]
    public async Task Search_Should_NotAskForOccupancy_When_NoDatesAreSent()
    {
        // Act
        await Search().HandleAsync(Query(checkIn: null, checkOut: null, pets: 3), CancellationToken.None);

        // Assert
        _search!.Filters.MinCapacity.Should().Be(3);
        _search.ExcludedListingIds.Should().BeNullOrEmpty();
        _search.Stay.Should().BeNull();
        _bookings.Verify(b => b.GetPeakOccupancyAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Search_Should_ReturnEveryDateError_When_DatesAreInvalid()
    {
        // Act
        var result = await Search().HandleAsync(Query(checkIn: "2026-10-01", checkOut: "amanhã", pets: 0), CancellationToken.None);

        // Assert
        result.Errors.Should().BeEquivalentTo([ListingsErrors.CheckInFilterInThePast, ListingsErrors.CheckOutFilterInvalid, ListingsErrors.PetsFilterInvalid]);
    }

    [Fact]
    public async Task Search_Should_ReturnRequiredError_When_OnlyOneDateIsSent()
    {
        // Act
        var result = await Search().HandleAsync(Query(checkOut: null), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.CheckOutFilterRequired);
    }

    [Fact]
    public async Task Search_Should_ReturnBadges_When_HostIsExperiencedFastAndNearby()
    {
        // Arrange: a busca é feita de um endereço na Zona 7, o mesmo bairro do cantinho.
        _bookings
            .Setup(b => b.GetTrackRecordsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, HostTrackRecord> { [ListingsTestData.IndividualHostId] = new(6, TimeSpan.FromMinutes(40)) });

        // Act
        var result = await Search().HandleAsync(Query(checkIn: null, checkOut: null), CancellationToken.None);

        // Assert
        result.Value!.Items.Single().Badges.Should().Equal(
            ListingBadges.Neighbor, ListingBadges.FastResponder, ListingBadges.Yard, ListingBadges.Experienced);
    }

    [Fact]
    public async Task GetById_Should_ReturnTheBadges()
    {
        // Arrange
        var sut = new GetListingByIdQueryHandler(_repository.Object, ListingsTestData.Hosts().Object, _bookings.Object, _addresses.Object, ListingsTestData.Reviews().Object);

        // Act
        var result = await sut.HandleAsync(new GetListingByIdQuery(ListingsTestData.OwnerUserId, Roles.Owner, _listing.Id.Value), CancellationToken.None);

        // Assert: sem histórico de reservas, só o que vem do cantinho e do endereço.
        result.Value!.Badges.Should().Equal(ListingBadges.Neighbor, ListingBadges.Yard);
        _addresses.Verify(a => a.GetAsync(ListingsTestData.OwnerUserId, Roles.Owner, It.IsAny<CancellationToken>()), Times.Once);
    }
}
