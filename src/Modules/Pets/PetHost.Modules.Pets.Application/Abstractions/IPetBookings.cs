namespace PetHost.Modules.Pets.Application.Abstractions;

/// <summary>
/// Porta para as reservas (módulo Bookings). §5: a Application declara a interface; a
/// Infrastructure implementa via <c>Shared.Contracts</c>.
/// </summary>
public interface IPetBookings
{
    /// <summary>O pet está numa reserva em aberto (solicitada, aceita, confirmada ou em andamento)?</summary>
    Task<bool> HasOpenBookingAsync(Guid petId, CancellationToken cancellationToken);

    /// <summary>
    /// O pet foi pedido para um cantinho do anfitrião (reserva solicitada, aceita, confirmada, em
    /// andamento ou concluída)? É o que libera a ficha completa para o anfitrião.
    /// </summary>
    Task<bool> IsBookedWithHostAsync(Guid petId, Guid hostId, CancellationToken cancellationToken);
}
