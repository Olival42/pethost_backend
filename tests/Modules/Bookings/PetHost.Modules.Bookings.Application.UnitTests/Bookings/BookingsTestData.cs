using Moq;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;

namespace PetHost.Modules.Bookings.Application.UnitTests.Bookings;

/// <summary>Contas de exemplo e os dublês das portas do módulo.</summary>
internal static class BookingsTestData
{
    /// <summary>Camila: a tutora das reservas do <see cref="BookingBuilder"/>.</summary>
    public static readonly Guid OwnerUserId = Guid.CreateVersion7();

    /// <summary>Dona Cida: a anfitriã do cantinho do <see cref="BookingBuilder"/>.</summary>
    public static readonly Guid HostUserId = Guid.CreateVersion7();

    /// <summary>Outra tutora, sem reserva nenhuma.</summary>
    public static readonly Guid OtherOwnerUserId = Guid.CreateVersion7();
    public static readonly Guid OtherOwnerId = Guid.CreateVersion7();

    /// <summary>Outro anfitrião, sem reserva nenhuma.</summary>
    public static readonly Guid OtherHostUserId = Guid.CreateVersion7();
    public static readonly Guid OtherHostId = Guid.CreateVersion7();

    /// <summary>Tutora inativa e anfitriã suspensa.</summary>
    public static readonly Guid InactiveOwnerUserId = Guid.CreateVersion7();
    public static readonly Guid SuspendedHostUserId = Guid.CreateVersion7();

    /// <summary>Conta sem perfil de tutor nem de anfitrião.</summary>
    public static readonly Guid NoProfileUserId = Guid.CreateVersion7();

    public const string OwnerPhone = "(44) 99999-0001";
    public const string HostPhone = "(44) 99999-0002";

    public const string OwnerEmail = "camila@pethost.test";
    public const string HostEmail = "cida@pethost.test";

    /// <summary>Endereço exato do cantinho da Dona Cida.</summary>
    public static readonly BookingAddress ListingAddress = new("87020000", "Rua das Flores", "120", null, "Zona 7", "Maringá", "PR");

    public static Mock<IBookingParties> Parties()
    {
        var parties = new Mock<IBookingParties>();

        parties.Setup(p => p.FindOwnerByAccountAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((PartyProfile?)null);
        parties.Setup(p => p.FindHostByAccountAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((PartyProfile?)null);

        parties.Setup(p => p.FindOwnerByAccountAsync(OwnerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyProfile(BookingBuilder.OwnerId, IsAvailable: true));
        parties.Setup(p => p.FindOwnerByAccountAsync(OtherOwnerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyProfile(OtherOwnerId, IsAvailable: true));
        parties.Setup(p => p.FindOwnerByAccountAsync(InactiveOwnerUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyProfile(Guid.CreateVersion7(), IsAvailable: false));

        parties.Setup(p => p.FindHostByAccountAsync(HostUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyProfile(BookingBuilder.HostId, IsAvailable: true));
        parties.Setup(p => p.FindHostByAccountAsync(OtherHostUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyProfile(OtherHostId, IsAvailable: true));
        parties.Setup(p => p.FindHostByAccountAsync(SuspendedHostUserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PartyProfile(BookingBuilder.HostId, IsAvailable: false));

        parties.Setup(p => p.GetOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                ids.ToDictionary(id => id, id => new PartyInfo(id, "Camila Souza", null, OwnerPhone, OwnerEmail)));
        parties.Setup(p => p.GetHostsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                ids.ToDictionary(id => id, id => new PartyInfo(id, "Dona Cida", "https://cdn.pethost.test/cida.png", HostPhone, HostEmail)));

        return parties;
    }

    public static Mock<IBookingListings> Listings(BookableListing? listing = null)
    {
        var listings = new Mock<IBookingListings>();

        listings.Setup(l => l.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((BookableListing?)null);
        listings.Setup(l => l.FindAsync(BookingBuilder.ListingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(listing ?? BookingBuilder.Listing());

        listings.Setup(l => l.GetSummariesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                ids.ToDictionary(id => id, id => new BookingListingSummary(id, "Casa com quintal", "Zona 7", "Maringá", "PR")));

        listings.Setup(l => l.GetAddressesAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => ids.ToDictionary(id => id, _ => ListingAddress));

        return listings;
    }

    public static Mock<IBookingPets> Pets(params BookablePet[] pets)
    {
        var known = pets.Length == 0 ? [BookingBuilder.Pipoca(), BookingBuilder.Thor()] : pets;

        var mock = new Mock<IBookingPets>();
        mock.Setup(p => p.GetAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) => [.. known.Where(pet => ids.Contains(pet.Id))]);

        return mock;
    }

    /// <summary>Unidade de trabalho que grava sem erro e roda o trabalho "travado" na hora.</summary>
    public static Mock<IBookingsUnitOfWork> UnitOfWork(Result? saveResult = null)
    {
        var unitOfWork = new Mock<IBookingsUnitOfWork>();

        unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(saveResult ?? Result.Success());
        unitOfWork
            .Setup(u => u.RunLockedOnListingAsync(It.IsAny<Guid>(), It.IsAny<Func<CancellationToken, Task<Result>>>(), It.IsAny<CancellationToken>()))
            .Returns((Guid _, Func<CancellationToken, Task<Result>> work, CancellationToken ct) => work(ct));

        return unitOfWork;
    }

    /// <summary>Repositório que acha <paramref name="bookings"/> pelo id e não tem nada ocupando vaga.</summary>
    public static Mock<IBookingRepository> Repository(params Booking[] bookings)
    {
        var repository = new Mock<IBookingRepository>();

        repository.Setup(r => r.GetByIdAsync(It.IsAny<BookingId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookingId id, CancellationToken _) => bookings.FirstOrDefault(b => b.Id == id));
        repository.Setup(r => r.ListOccupyingAsync(It.IsAny<Guid>(), It.IsAny<StayPeriod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        repository.Setup(r => r.ListPetsBookedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<StayPeriod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        return repository;
    }

    public static BookingResponseBuilder ResponseBuilder() =>
        new(Listings().Object, Parties().Object, Pets().Object);

    /// <summary>Avisos por e-mail sobre o <paramref name="notifier"/> (um dublê que não manda nada, se omitido).</summary>
    public static BookingNotifications Notifications(IBookingNotifier? notifier = null) =>
        new(Parties().Object, Listings().Object, notifier ?? new Mock<IBookingNotifier>().Object);
}
