namespace PetHost.Modules.Bookings.Domain.Bookings;

/// <summary>Acesso a <see cref="Booking"/>. A Application só conhece esta interface (§11).</summary>
public interface IBookingRepository
{
    /// <summary>Pelo id, com os pets. Rastreado: quem chama pode alterar e gravar.</summary>
    Task<Booking?> GetByIdAsync(BookingId id, CancellationToken cancellationToken);

    /// <summary>
    /// As reservas do cantinho que ocupam vaga (Confirmada ou Em andamento) e dividem ao menos
    /// uma noite com <paramref name="period"/> — a base da conta de capacidade.
    /// </summary>
    Task<IReadOnlyList<Booking>> ListOccupyingAsync(Guid listingId, StayPeriod period, CancellationToken cancellationToken);

    /// <summary>
    /// Quais dos <paramref name="petIds"/> já estão em outra reserva em aberto (Solicitada,
    /// Aceita, Confirmada ou Em andamento) que divide noite com <paramref name="period"/>: o pet
    /// não pode estar em duas casas ao mesmo tempo.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListPetsBookedAsync(
        IReadOnlyCollection<Guid> petIds,
        StayPeriod period,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reservas do tutor ou do anfitrião (<see cref="BookingSearch"/>), paginadas, da chegada mais
    /// recente para a mais antiga.
    /// </summary>
    Task<(IReadOnlyList<Booking> Items, long TotalCount)> SearchAsync(BookingSearch search, CancellationToken cancellationToken);

    /// <summary>
    /// Pedidos ainda sem resposta (Solicitada) pedidos até <paramref name="requestedBefore"/> ou
    /// com chegada antes de <paramref name="today"/> — o prazo de resposta venceu. No máximo
    /// <paramref name="limit"/>, os mais antigos primeiro. Rastreados.
    /// </summary>
    Task<IReadOnlyList<Booking>> ListOverdueRequestsAsync(
        DateTimeOffset requestedBefore,
        DateOnly today,
        int limit,
        CancellationToken cancellationToken);

    void Add(Booking booking);
}
