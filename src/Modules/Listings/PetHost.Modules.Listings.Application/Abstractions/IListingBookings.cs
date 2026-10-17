namespace PetHost.Modules.Listings.Application.Abstractions;

/// <summary>
/// Porta para as reservas (módulo Bookings): vaga por período, os números dos selos e se o
/// cantinho tem reserva em aberto (que barra a inativação). §5: a
/// Application declara a interface; a Infrastructure implementa via <c>Shared.Contracts</c>.
/// </summary>
public interface IListingBookings
{
    /// <summary>
    /// Para cada cantinho com estadias que ocupam vaga no período, o maior número de pets numa
    /// mesma noite. Cantinho vazio no período não aparece.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> GetPeakOccupancyAsync(DateOnly checkIn, DateOnly checkOut, CancellationToken cancellationToken);

    /// <summary>
    /// Os pets hospedados no cantinho no período (reservas aceitas, confirmadas ou em andamento que
    /// cruzam as noites de <paramref name="checkIn"/> a <paramref name="checkOut"/>), com o tutor de
    /// cada reserva. É a base dos "outros hóspedes" do "Combina com seu pet".
    /// </summary>
    Task<IReadOnlyList<ListingGuest>> GetGuestsAsync(Guid listingId, DateOnly checkIn, DateOnly checkOut, CancellationToken cancellationToken);

    /// <summary>O cantinho tem reserva em aberto (solicitada, aceita, confirmada ou em andamento)?</summary>
    Task<bool> HasOpenBookingAsync(Guid listingId, CancellationToken cancellationToken);

    /// <summary>Números de cada anfitrião (estadias concluídas, tempo médio de resposta). Sem reserva, não aparece.</summary>
    Task<IReadOnlyDictionary<Guid, HostTrackRecord>> GetTrackRecordsAsync(
        IReadOnlyCollection<Guid> hostIds,
        CancellationToken cancellationToken);
}

/// <summary>Um pet hospedado no cantinho no período.</summary>
/// <param name="OwnerId">Tutor da reserva (<c>owner.owners.id</c>).</param>
public sealed record ListingGuest(Guid OwnerId, Guid PetId);

/// <summary>O histórico do anfitrião nas reservas, para os selos.</summary>
/// <param name="AverageResponseTime">Nulo se ele nunca respondeu a um pedido.</param>
public sealed record HostTrackRecord(int CompletedStays, TimeSpan? AverageResponseTime)
{
    /// <summary>Anfitrião sem nenhuma reserva.</summary>
    public static HostTrackRecord None { get; } = new(0, null);
}
