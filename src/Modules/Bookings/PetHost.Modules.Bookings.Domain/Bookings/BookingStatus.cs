namespace PetHost.Modules.Bookings.Domain.Bookings;

/// <summary>
/// Ciclo da reserva (escopo, seção 5): Solicitada → Aceita → Confirmada → Em andamento →
/// Concluída, ou um dos fins antecipados (Recusada, Expirada, Cancelada).
/// </summary>
public enum BookingStatus
{
    /// <summary>O tutor pediu; o anfitrião tem 24h para responder.</summary>
    Requested,

    /// <summary>
    /// O anfitrião aceitou; o tutor tem 24h para pagar. Ainda sem uso: enquanto o módulo de
    /// pagamento não existe, o aceite já confirma (<see cref="Booking.Accept"/>).
    /// </summary>
    Accepted,

    /// <summary>Pagamento confirmado (hoje: aceite do anfitrião). Ocupa a capacidade do cantinho.</summary>
    Confirmed,

    /// <summary>O anfitrião marcou "chegou" (check-in): o pet está com ele.</summary>
    InProgress,

    /// <summary>O anfitrião marcou "voltou pra casa" (check-out): o pet está com o tutor. Fim.</summary>
    Completed,

    /// <summary>O anfitrião recusou o pedido. Fim.</summary>
    Declined,

    /// <summary>Passou o prazo de resposta (ou de pagamento) sem ação. Fim.</summary>
    Expired,

    /// <summary>O tutor ou o anfitrião cancelou antes da chegada. Fim.</summary>
    Cancelled,
}
