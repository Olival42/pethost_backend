using Moq;
using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Application.Listings;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.TestKit;

namespace PetHost.Modules.Listings.Application.UnitTests.Listings;

/// <summary>Contas e anfitriões de exemplo, e os dublês das portas do módulo.</summary>
internal static class ListingsTestData
{
    /// <summary>Dona Cida: anfitriã pessoa física.</summary>
    public static readonly Guid IndividualUserId = Guid.CreateVersion7();
    public static readonly Guid IndividualHostId = Guid.CreateVersion7();

    /// <summary>Hotel Pet Feliz: anfitrião empresa, com filiais.</summary>
    public static readonly Guid CompanyUserId = Guid.CreateVersion7();
    public static readonly Guid CompanyHostId = Guid.CreateVersion7();

    /// <summary>Anfitrião suspenso.</summary>
    public static readonly Guid SuspendedUserId = Guid.CreateVersion7();
    public static readonly Guid SuspendedHostId = Guid.CreateVersion7();

    /// <summary>Tutora (sem perfil de anfitrião).</summary>
    public static readonly Guid OwnerUserId = Guid.CreateVersion7();

    public static Mock<IListingHosts> Hosts()
    {
        var hosts = new Mock<IListingHosts>();

        hosts.Setup(h => h.FindByAccountAsync(IndividualUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostProfile(IndividualHostId, IsCompany: false, IsAvailable: true, PayoutsEnabled: true));
        hosts.Setup(h => h.FindByAccountAsync(CompanyUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostProfile(CompanyHostId, IsCompany: true, IsAvailable: true, PayoutsEnabled: true));
        hosts.Setup(h => h.FindByAccountAsync(SuspendedUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HostProfile(SuspendedHostId, IsCompany: false, IsAvailable: false, PayoutsEnabled: true));
        hosts.Setup(h => h.FindByAccountAsync(OwnerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HostProfile?)null);

        hosts
            .Setup(h => h.GetSummariesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                ids.ToDictionary(id => id, Summary));

        hosts.Setup(h => h.GetUnavailableIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([SuspendedHostId]);

        return hosts;
    }

    /// <summary>Reservas: nenhum cantinho ocupado e nenhum anfitrião com histórico.</summary>
    public static Mock<IListingBookings> Bookings()
    {
        var bookings = new Mock<IListingBookings>();

        bookings
            .Setup(b => b.GetPeakOccupancyAsync(It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, int>());
        bookings
            .Setup(b => b.GetTrackRecordsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, HostTrackRecord>());

        return bookings;
    }

    /// <summary>Geocodificador que sempre acha o centro de Maringá.</summary>
    public static Mock<IGeocoder> Geocoder(GeocodingResult? result = null)
    {
        var geocoder = new Mock<IGeocoder>();
        geocoder
            .Setup(g => g.LocateAsync(It.IsAny<ListingAddressData>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result ?? GeocodingResult.Found(ListingBuilder.MaringaLatitude, ListingBuilder.MaringaLongitude));

        return geocoder;
    }

    public static Listing IndividualListing() => new ListingBuilder().OwnedBy(IndividualHostId).Build();

    public static Listing CompanyListing() => new ListingBuilder().OwnedBy(CompanyHostId, isCompany: true).Build();

    public static ListingAddressInput Address() =>
        new("87020-000", "Rua das Flores", "120", null, "Zona 7", "Maringá", "PR");

    private static ListingHostSummary Summary(Guid hostId) =>
        hostId == CompanyHostId
            ? new ListingHostSummary(hostId, "Hotel Pet Feliz", null, IsCompany: true, IsAvailable: true, PayoutsEnabled: true)
            : new ListingHostSummary(hostId, "Dona Cida", "https://cdn.pethost.test/cida.png", IsCompany: false, IsAvailable: hostId != SuspendedHostId, PayoutsEnabled: true);
}
