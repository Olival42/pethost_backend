using PetHost.Modules.Bookings.Domain.Bookings;

namespace PetHost.TestKit;

/// <summary>
/// Monta uma reserva para teste: por padrão, a Camila pede 5 noites (20 a 25 de outubro de 2026)
/// para a Pipoca (cachorro médio) no cantinho da Dona Cida, que recebe todos os dias, cabe 3 pets
/// e cobra R$ 60 a diária. Cada <c>With...</c> troca uma parte; o teste declara só o que importa.
/// </summary>
public sealed class BookingBuilder
{
    /// <summary>Domingo, 11/10/2026, 09:00 em Brasília.</summary>
    public static readonly DateTimeOffset DefaultNow = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly DefaultCheckIn = new(2026, 10, 20);
    public static readonly DateOnly DefaultCheckOut = new(2026, 10, 25);

    public static readonly Guid OwnerId = Guid.Parse("0199a000-0000-7000-8000-000000000001");
    public static readonly Guid HostId = Guid.Parse("0199a000-0000-7000-8000-000000000002");
    public static readonly Guid ListingId = Guid.Parse("0199a000-0000-7000-8000-000000000003");
    public static readonly Guid PipocaId = Guid.Parse("0199a000-0000-7000-8000-000000000004");
    public static readonly Guid ThorId = Guid.Parse("0199a000-0000-7000-8000-000000000005");

    private BookableListing _listing = Listing();
    private List<BookablePet> _pets = [Pipoca()];
    private DateOnly _checkIn = DefaultCheckIn;
    private DateOnly _checkOut = DefaultCheckOut;
    private DateTimeOffset _now = DefaultNow;

    /// <summary>O cantinho da Dona Cida: recebe todos os dias, cachorro e gato P/M, 3 pets, R$ 60.</summary>
    public static BookableListing Listing() => new(
        ListingId,
        HostId,
        IsBookable: true,
        Capacity: 3,
        NightlyRate: 60.00m,
        AcceptedSpecies: ["dog", "cat"],
        AcceptsSmall: true,
        AcceptsMedium: true,
        AcceptsLarge: false,
        AvailableWeekdays: Enum.GetValues<DayOfWeek>());

    /// <summary>A Pipoca: cachorro médio da Camila.</summary>
    public static BookablePet Pipoca() => new(PipocaId, OwnerId, "Pipoca", "dog", "medium", IsActive: true);

    /// <summary>O Thor: cachorro pequeno da Camila.</summary>
    public static BookablePet Thor() => new(ThorId, OwnerId, "Thor", "dog", "small", IsActive: true);

    /// <summary>O período padrão (20 a 25/10), conferido em <see cref="DefaultNow"/>.</summary>
    public static StayPeriod Period(DateOnly? checkIn = null, DateOnly? checkOut = null) =>
        StayPeriod.Create(checkIn ?? DefaultCheckIn, checkOut ?? DefaultCheckOut, BookingClock.Today(DefaultNow)).Value!;

    public BookingBuilder WithListing(Func<BookableListing, BookableListing> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        _listing = change(_listing);
        return this;
    }

    public BookingBuilder WithPets(params BookablePet[] pets)
    {
        _pets = [.. pets];
        return this;
    }

    public BookingBuilder WithPeriod(DateOnly checkIn, DateOnly checkOut)
    {
        _checkIn = checkIn;
        _checkOut = checkOut;
        return this;
    }

    /// <summary>Quando o pedido é feito.</summary>
    public BookingBuilder RequestedAt(DateTimeOffset now)
    {
        _now = now;
        return this;
    }

    /// <summary>A reserva Solicitada. Falha do domínio vira exceção: o builder só monta caso válido.</summary>
    public Booking Build()
    {
        var period = StayPeriod.Create(_checkIn, _checkOut, BookingClock.Today(_now));
        if (period.IsFailure)
            throw new InvalidOperationException($"Invalid test period: {period.FirstError!.Message}");

        var booking = Booking.Request(
            OwnerId,
            _listing,
            period.Value!,
            [.. _pets.Select(p => p.Id)],
            _pets,
            [],
            ListingOccupancy.Empty,
            _now);

        return booking.IsSuccess
            ? booking.Value!
            : throw new InvalidOperationException($"Invalid test booking: {booking.FirstError!.Message}");
    }

    /// <summary>A reserva aceita pelo anfitrião (esperando a 1ª parcela), 1h depois do pedido.</summary>
    public Booking BuildAccepted()
    {
        var booking = Build();
        var accepted = booking.Accept(_listing, ListingOccupancy.Empty, _now.AddHours(1));

        return accepted.IsSuccess
            ? booking
            : throw new InvalidOperationException($"Could not accept test booking: {accepted.FirstError!.Message}");
    }

    /// <summary>A reserva confirmada: aceita e com a 1ª parcela paga, 2h depois do pedido.</summary>
    public Booking BuildConfirmed()
    {
        var booking = BuildAccepted();
        var confirmed = booking.ConfirmPayment(_now.AddHours(2));

        return confirmed.IsSuccess
            ? booking
            : throw new InvalidOperationException($"Could not confirm test booking: {confirmed.FirstError!.Message}");
    }

    /// <summary>A reserva com check-in feito (Em andamento), no dia da chegada.</summary>
    public Booking BuildInProgress()
    {
        var booking = BuildConfirmed();
        var checkedIn = booking.CheckIn(BookingClock.StartOf(_checkIn).AddHours(10));

        return checkedIn.IsSuccess
            ? booking
            : throw new InvalidOperationException($"Could not check in test booking: {checkedIn.FirstError!.Message}");
    }
}
