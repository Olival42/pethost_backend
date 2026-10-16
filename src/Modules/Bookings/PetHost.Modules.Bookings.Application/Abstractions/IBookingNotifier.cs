using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;

namespace PetHost.Modules.Bookings.Application.Abstractions;

/// <summary>Avisos da reserva (e-mail, em segundo plano).</summary>
public interface IBookingNotifier
{
    /// <summary>
    /// Ao anfitrião: chegou um pedido de estadia; responda no prazo de 24h (senão o pedido expira).
    /// Sem telefone nem endereço do tutor — o contato só sai depois do pagamento.
    /// </summary>
    Task NotifyRequestedAsync(BookingNotice notice, CancellationToken cancellationToken);

    /// <summary>Ao tutor: o anfitrião aceitou o pedido; pague a 1ª parcela no prazo.</summary>
    Task NotifyAcceptedAsync(BookingNotice notice, CancellationToken cancellationToken);

    /// <summary>Ao tutor: o anfitrião recusou o pedido (com o motivo, se houver).</summary>
    Task NotifyDeclinedAsync(BookingNotice notice, CancellationToken cancellationToken);

    /// <summary>
    /// A reserva está confirmada (1ª parcela paga). Ao tutor, com o endereço exato e o telefone do
    /// anfitrião; ao anfitrião, com o telefone do tutor — o pet vem na data combinada.
    /// </summary>
    Task NotifyConfirmedAsync(BookingNotice notice, CancellationToken cancellationToken);

    /// <summary>Ao tutor e ao anfitrião, cada um no seu e-mail: a reserva foi cancelada (por um deles ou pela plataforma).</summary>
    Task NotifyCancelledAsync(BookingNotice notice, CancellationToken cancellationToken);

    /// <summary>
    /// Véspera do check-in. Ao tutor, com a data, o endereço exato e o telefone do anfitrião; ao
    /// anfitrião, com a data, os pets e o telefone do tutor.
    /// </summary>
    Task NotifyCheckInReminderAsync(BookingNotice notice, CancellationToken cancellationToken);

    /// <summary>
    /// Ao anfitrião: chegou o dia de saída e o check-out não foi feito. Marque "voltou pra casa" — sem
    /// isso, a plataforma conclui a estadia sozinha alguns dias depois.
    /// </summary>
    Task NotifyCheckOutReminderAsync(BookingNotice notice, CancellationToken cancellationToken);

    /// <summary>Ao tutor e ao anfitrião: a plataforma concluiu a estadia (o check-out não foi feito a tempo).</summary>
    Task NotifyAutoCheckedOutAsync(BookingNotice notice, CancellationToken cancellationToken);
}

/// <summary>Um aviso da reserva para um dos lados.</summary>
/// <param name="Email">Para onde vai.</param>
/// <param name="Name">Nome de quem recebe.</param>
/// <param name="Recipient">Quem recebe: o tutor ou o anfitrião (muda o texto).</param>
/// <param name="Booking">A reserva como ficou depois da mudança.</param>
/// <param name="OtherPartyName">Nome do outro lado.</param>
/// <param name="OtherPartyPhone">Telefone do outro lado — só em reserva que libera contato.</param>
/// <param name="Address">Endereço exato do cantinho — só para o tutor, em reserva que libera contato.</param>
public sealed record BookingNotice(
    string Email,
    string Name,
    BookingParty Recipient,
    BookingResponse Booking,
    string? OtherPartyName,
    string? OtherPartyPhone,
    BookingAddress? Address = null);
