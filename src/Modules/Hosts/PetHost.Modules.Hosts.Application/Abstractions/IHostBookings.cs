using PetHost.Shared.Contracts.Bookings;

namespace PetHost.Modules.Hosts.Application.Abstractions;

/// <summary>
/// Porta para as reservas (módulo Bookings). §5: a Application declara a interface; a
/// Infrastructure implementa via <c>Shared.Contracts</c>.
/// </summary>
public interface IHostBookings
{
    /// <summary>O anfitrião tem reserva em aberto (solicitada, aceita, confirmada ou em andamento)?</summary>
    Task<bool> HasOpenBookingAsync(Guid hostId, CancellationToken cancellationToken);

    /// <summary>
    /// Ids das reservas em aberto (solicitada, aceita, confirmada ou em andamento) do anfitrião, da
    /// chegada mais próxima para a mais distante.
    /// </summary>
    Task<IReadOnlyList<Guid>> ListOpenBookingIdsAsync(Guid hostId, CancellationToken cancellationToken);

    /// <summary>
    /// As reservas do anfitrião para o painel: pedidos novos, aceitas esperando pagamento, estadias
    /// em andamento e próximas estadias (totais e, nas listas, até <paramref name="itemLimit"/> itens).
    /// </summary>
    Task<HostBookingsOverview> GetOverviewAsync(Guid hostId, int itemLimit, CancellationToken cancellationToken);

    /// <summary>Estadias do anfitrião concluídas (check-out) entre <paramref name="from"/> (inclusive) e <paramref name="to"/> (exclusive).</summary>
    Task<int> CountCompletedStaysAsync(Guid hostId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>
    /// O admin suspendeu o anfitrião: as reservas dele que ainda não começaram são canceladas pela
    /// plataforma (a paga, com 100% de reembolso ao tutor). Devolve quantas. <b>Nunca falha</b> a
    /// suspensão: erro vai para o log e suspender de novo completa.
    /// </summary>
    Task<int> CancelForSuspensionAsync(Guid hostId, CancellationToken cancellationToken);
}
