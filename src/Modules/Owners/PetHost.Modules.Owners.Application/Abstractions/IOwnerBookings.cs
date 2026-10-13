namespace PetHost.Modules.Owners.Application.Abstractions;

/// <summary>
/// Porta para as reservas (módulo Bookings). §5: a Application declara a interface; a
/// Infrastructure implementa via <c>Shared.Contracts</c>.
/// </summary>
public interface IOwnerBookings
{
    /// <summary>O tutor tem reserva em aberto (solicitada, aceita, confirmada ou em andamento)?</summary>
    Task<bool> HasOpenBookingAsync(Guid ownerId, CancellationToken cancellationToken);
}
