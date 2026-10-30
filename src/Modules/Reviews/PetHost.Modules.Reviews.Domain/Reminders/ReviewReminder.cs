using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Domain.Reminders;

/// <summary>
/// O lembrete de "avalie a estadia" já mandado a um lado da reserva: um por lado, nunca repetido.
/// Espelha a tabela <c>review.review_reminders</c>.
/// </summary>
public sealed class ReviewReminder
{
    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private ReviewReminder()
    {
    }

    public ReviewReminder(Guid bookingId, ReviewParty side, DateTimeOffset sentAt)
    {
        if (bookingId == Guid.Empty)
            throw new ArgumentException("The booking id cannot be empty.", nameof(bookingId));

        BookingId = bookingId;
        Side = side;
        SentAt = sentAt;
    }

    /// <summary>A reserva (<c>booking.bookings.id</c>).</summary>
    public Guid BookingId { get; private set; }

    /// <summary>Quem recebeu o lembrete: o tutor ou o anfitrião.</summary>
    public ReviewParty Side { get; private set; }

    /// <summary>Quando o lembrete foi para a caixa de saída.</summary>
    public DateTimeOffset SentAt { get; private set; }
}
