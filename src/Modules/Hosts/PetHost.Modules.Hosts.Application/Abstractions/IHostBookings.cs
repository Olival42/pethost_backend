namespace PetHost.Modules.Hosts.Application.Abstractions;

/// <summary>
/// Porta para as reservas (módulo Bookings). §5: a Application declara a interface; a
/// Infrastructure implementa via <c>Shared.Contracts</c>.
/// </summary>
public interface IHostBookings
{
    /// <summary>O anfitrião tem reserva em aberto (solicitada, aceita, confirmada ou em andamento)?</summary>
    Task<bool> HasOpenBookingAsync(Guid hostId, CancellationToken cancellationToken);
}
