namespace PetHost.Modules.Conversations.Application.Abstractions;

/// <summary>Porta para as reservas entre o tutor e o anfitrião da conversa (módulo Bookings).</summary>
public interface IConversationBookings
{
    /// <summary>
    /// A reserva em andamento (check-in feito) entre os dois, para ligar a mensagem ao histórico
    /// dela. <c>null</c> se não há.
    /// </summary>
    Task<Guid?> GetInProgressBookingIdAsync(Guid ownerId, Guid hostId, CancellationToken cancellationToken);

    /// <summary>
    /// Os dois têm reserva em aberto (solicitada, aceita, confirmada ou em andamento)? Conversa
    /// assim não é apagada pela retenção.
    /// </summary>
    Task<bool> HasOpenBookingAsync(Guid ownerId, Guid hostId, CancellationToken cancellationToken);
}
