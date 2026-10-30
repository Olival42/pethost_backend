namespace PetHost.Modules.Owners.Application.Abstractions;

/// <summary>
/// Porta para as reservas (módulo Bookings). §5: a Application declara a interface; a
/// Infrastructure implementa via <c>Shared.Contracts</c>.
/// </summary>
public interface IOwnerBookings
{
    /// <summary>O tutor tem reserva em aberto (solicitada, aceita, confirmada ou em andamento)?</summary>
    Task<bool> HasOpenBookingAsync(Guid ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// O admin suspendeu o tutor: as reservas dele que ainda não começaram são canceladas pela
    /// plataforma (a paga, sem reembolso). Devolve quantas. <b>Nunca falha</b> a suspensão: erro vai
    /// para o log e suspender de novo completa.
    /// </summary>
    Task<int> CancelForSuspensionAsync(Guid ownerId, CancellationToken cancellationToken);
}
