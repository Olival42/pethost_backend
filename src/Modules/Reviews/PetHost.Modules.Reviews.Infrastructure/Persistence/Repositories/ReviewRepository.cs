using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Infrastructure.Persistence.Repositories;

/// <summary>Implementação de <see cref="IReviewRepository"/> sobre o EF Core.</summary>
internal sealed class ReviewRepository(ReviewsDbContext dbContext) : IReviewRepository
{
    public Task<Review?> GetByIdAsync(ReviewId id, CancellationToken cancellationToken) =>
        dbContext.Reviews.FirstOrDefaultAsync(r => r.Id == id && r.DeletedAt == null, cancellationToken);

    public Task<bool> ExistsForBookingAsync(Guid bookingId, ReviewParty side, CancellationToken cancellationToken) =>
        dbContext.Reviews.AsNoTracking().AnyAsync(r => r.BookingId == bookingId && r.AuthorSide == side, cancellationToken);

    public async Task<IReadOnlySet<(Guid BookingId, ReviewParty Side)>> GetReviewedSidesAsync(
        IReadOnlyCollection<Guid> bookingIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bookingIds);

        var ids = bookingIds.ToList();
        var rows = await dbContext.Reviews
            .AsNoTracking()
            .Where(r => ids.Contains(r.BookingId))
            .Select(r => new { r.BookingId, r.AuthorSide })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(r => (r.BookingId, r.AuthorSide)).ToHashSet();
    }

    public Task<Review?> FindByBookingAsync(Guid bookingId, ReviewParty side, CancellationToken cancellationToken) =>
        dbContext.Reviews.FirstOrDefaultAsync(
            r => r.BookingId == bookingId && r.AuthorSide == side && r.DeletedAt == null,
            cancellationToken);

    public async Task<IReadOnlyList<Review>> ListPublishedToNotifyAsync(DateTimeOffset now, int take, CancellationToken cancellationToken) =>
        await dbContext.Reviews
            .Where(r => r.PublishedAt <= now && r.SubjectNotifiedAt == null && r.DeletedAt == null && r.HiddenAt == null)
            .OrderBy(r => r.PublishedAt)
            .ThenBy(r => r.Id)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(Review review) => dbContext.Reviews.Add(review);
}
