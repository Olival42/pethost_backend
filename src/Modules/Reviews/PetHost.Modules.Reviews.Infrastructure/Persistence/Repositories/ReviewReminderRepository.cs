using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Reviews.Domain.Reminders;
using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Infrastructure.Persistence.Repositories;

/// <summary>Implementação de <see cref="IReviewReminderRepository"/> sobre o EF Core.</summary>
internal sealed class ReviewReminderRepository(ReviewsDbContext dbContext) : IReviewReminderRepository
{
    public async Task<IReadOnlySet<(Guid BookingId, ReviewParty Side)>> GetSentAsync(
        IReadOnlyCollection<Guid> bookingIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bookingIds);

        var ids = bookingIds.ToList();
        var rows = await dbContext.ReviewReminders
            .AsNoTracking()
            .Where(r => ids.Contains(r.BookingId))
            .Select(r => new { r.BookingId, r.Side })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(r => (r.BookingId, r.Side)).ToHashSet();
    }

    public void Add(ReviewReminder reminder) => dbContext.ReviewReminders.Add(reminder);
}
