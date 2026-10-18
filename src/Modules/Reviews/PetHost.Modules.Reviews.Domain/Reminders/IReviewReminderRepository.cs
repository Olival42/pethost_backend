using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Domain.Reminders;

/// <summary>Os lembretes já mandados. A Application só conhece esta interface (§11).</summary>
public interface IReviewReminderRepository
{
    /// <summary>Os lados que já receberam lembrete, de cada reserva de <paramref name="bookingIds"/>.</summary>
    Task<IReadOnlySet<(Guid BookingId, ReviewParty Side)>> GetSentAsync(
        IReadOnlyCollection<Guid> bookingIds,
        CancellationToken cancellationToken);

    void Add(ReviewReminder reminder);
}
