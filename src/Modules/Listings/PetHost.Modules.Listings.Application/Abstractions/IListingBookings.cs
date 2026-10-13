namespace PetHost.Modules.Listings.Application.Abstractions;

/// <summary>
/// Porta para as reservas (módulo Bookings): vaga por período e os números dos selos. §5: a
/// Application declara a interface; a Infrastructure implementa via <c>Shared.Contracts</c>.
/// </summary>
public interface IListingBookings
{
    /// <summary>
    /// Para cada cantinho com estadias que ocupam vaga no período, o maior número de pets numa
    /// mesma noite. Cantinho vazio no período não aparece.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> GetPeakOccupancyAsync(DateOnly checkIn, DateOnly checkOut, CancellationToken cancellationToken);

    /// <summary>Números de cada anfitrião (estadias concluídas, tempo médio de resposta). Sem reserva, não aparece.</summary>
    Task<IReadOnlyDictionary<Guid, HostTrackRecord>> GetTrackRecordsAsync(
        IReadOnlyCollection<Guid> hostIds,
        CancellationToken cancellationToken);
}

/// <summary>O histórico do anfitrião nas reservas, para os selos.</summary>
/// <param name="AverageResponseTime">Nulo se ele nunca respondeu a um pedido.</param>
public sealed record HostTrackRecord(int CompletedStays, TimeSpan? AverageResponseTime)
{
    /// <summary>Anfitrião sem nenhuma reserva.</summary>
    public static HostTrackRecord None { get; } = new(0, null);
}
