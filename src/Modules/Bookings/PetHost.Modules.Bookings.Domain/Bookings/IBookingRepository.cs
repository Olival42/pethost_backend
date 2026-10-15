namespace PetHost.Modules.Bookings.Domain.Bookings;

/// <summary>Acesso a <see cref="Booking"/>. A Application só conhece esta interface (§11).</summary>
public interface IBookingRepository
{
    /// <summary>Pelo id, com os pets. Rastreado: quem chama pode alterar e gravar.</summary>
    Task<Booking?> GetByIdAsync(BookingId id, CancellationToken cancellationToken);

    /// <summary>
    /// As reservas do cantinho que ocupam vaga (Aceita, Confirmada ou Em andamento) e dividem ao menos
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
    /// Reservas cujo prazo venceu: pedidos ainda sem resposta (Solicitada) pedidos até
    /// <paramref name="requestedBefore"/> ou com chegada até <paramref name="requestedCheckInUntil"/>
    /// (já não dá para aceitar com 48h de antecedência), e aceites ainda sem pagamento (Aceita)
    /// aceitos até <paramref name="acceptedBefore"/> ou com chegada antes de <paramref name="today"/>.
    /// No máximo <paramref name="limit"/>, os mais antigos primeiro. Rastreadas.
    /// </summary>
    Task<IReadOnlyList<Booking>> ListOverdueAsync(
        DateTimeOffset requestedBefore,
        DateOnly requestedCheckInUntil,
        DateTimeOffset acceptedBefore,
        DateOnly today,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ids das reservas do tutor (<paramref name="party"/> = tutor, <c>owner.owners.id</c>) ou do
    /// anfitrião (<c>host.hosts.id</c>) que ainda podem ser canceladas: Solicitada, Aceita ou
    /// Confirmada. Base do cancelamento quando o admin suspende um dos lados.
    /// </summary>
    Task<IReadOnlyList<BookingId>> ListCancellableIdsAsync(BookingParty party, Guid partyId, CancellationToken cancellationToken);

    /// <summary>
    /// Estadias que já deviam ter acabado em <paramref name="today"/>: Confirmadas (sem check-in) e
    /// Em andamento com o dia de saída até hoje — estas só se o lembrete do check-out ainda não foi
    /// ou se já passaram <see cref="Booking.AutoCheckOutAfterDays"/> dias. No máximo
    /// <paramref name="limit"/>, as mais antigas primeiro. Rastreadas.
    /// </summary>
    Task<IReadOnlyList<Booking>> ListOverdueStaysAsync(DateOnly today, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Reservas Confirmadas com chegada em <paramref name="checkIn"/> que ainda não tiveram o lembrete
    /// da véspera. No máximo <paramref name="limit"/>. Rastreadas.
    /// </summary>
    Task<IReadOnlyList<Booking>> ListCheckInsToRemindAsync(DateOnly checkIn, int limit, CancellationToken cancellationToken);

    void Add(Booking booking);
}
