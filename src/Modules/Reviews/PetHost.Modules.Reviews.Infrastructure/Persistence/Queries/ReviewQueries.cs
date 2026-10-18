using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reports.Responses;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Infrastructure.Persistence.Queries;

/// <summary>
/// Leitura do módulo (§11): sem rastreamento; as apagadas nunca aparecem. As notas dos pets vêm
/// junto (tabela dona). Médias calculadas no banco, só das visíveis: publicadas (às cegas, o
/// <c>published_at</c> já passou) e sem ocultas.
/// </summary>
internal sealed class ReviewQueries(ReviewsDbContext dbContext, TimeProvider timeProvider) : IReviewQueries
{
    public async Task<(IReadOnlyList<ReviewRow> Items, long TotalCount)> ListAsync(
        ReviewFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var reviews = NotDeleted().Where(r => r.AuthorSide == filter.AuthorSide);

        if (!filter.IncludeHidden)
            reviews = reviews.Where(r => r.HiddenAt == null);

        if (!filter.IncludeUnpublished)
        {
            var now = timeProvider.GetUtcNow();
            reviews = reviews.Where(r => r.PublishedAt <= now);
        }

        if (filter.HostId is { } hostId)
            reviews = reviews.Where(r => r.HostId == hostId);

        if (filter.ListingId is { } listingId)
            reviews = reviews.Where(r => r.ListingId == listingId);

        if (filter.OwnerId is { } ownerId)
            reviews = reviews.Where(r => r.OwnerId == ownerId);

        if (filter.PetId is { } petId)
            reviews = reviews.Where(r => r.PetRatings.Any(p => p.PetId == petId));

        var total = await reviews.LongCountAsync(cancellationToken).ConfigureAwait(false);
        if (total == 0)
            return ([], 0);

        var items = await reviews
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return ([.. items.Select(ReviewResponseBuilder.ToRow)], total);
    }

    public async Task<ReviewRow?> GetAsync(Guid reviewId, CancellationToken cancellationToken)
    {
        var id = new ReviewId(reviewId);
        var review = await NotDeleted().FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);

        return review is null ? null : ReviewResponseBuilder.ToRow(review);
    }

    public async Task<IReadOnlyDictionary<Guid, ReviewRow>> GetManyAsync(IReadOnlyCollection<Guid> reviewIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reviewIds);

        if (reviewIds.Count == 0)
            return new Dictionary<Guid, ReviewRow>();

        var ids = reviewIds.Select(id => new ReviewId(id)).ToList();
        var reviews = await NotDeleted().Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken).ConfigureAwait(false);

        return reviews.ToDictionary(r => r.Id.Value, ReviewResponseBuilder.ToRow);
    }

    public async Task<IReadOnlyDictionary<Guid, RatingRow>> GetRatingsAsync(
        RatingScope scope,
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
            return new Dictionary<Guid, RatingRow>();

        var keys = ids.ToList();
        var now = timeProvider.GetUtcNow();
        var authorSide = scope is RatingScope.Owner or RatingScope.Pet ? ReviewParty.Host : ReviewParty.Owner;
        var visible = NotDeleted().Where(r => r.HiddenAt == null && r.PublishedAt <= now && r.AuthorSide == authorSide);

        if (scope == RatingScope.Pet)
            return await PetRatingsAsync(visible, keys, cancellationToken).ConfigureAwait(false);

        var grouped = scope switch
        {
            RatingScope.Host => visible.Where(r => keys.Contains(r.HostId)).GroupBy(r => r.HostId),
            RatingScope.Listing => visible.Where(r => keys.Contains(r.ListingId)).GroupBy(r => r.ListingId),
            _ => visible.Where(r => keys.Contains(r.OwnerId)).GroupBy(r => r.OwnerId),
        };

        var rows = await grouped
            .Select(g => new { g.Key, Average = g.Average(r => (double)r.Rating), Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(
            r => r.Key,
            r => new RatingRow(Math.Round((decimal)r.Average, 2, MidpointRounding.AwayFromZero), r.Count));
    }

    public async Task<(IReadOnlyList<ReviewReportRow> Items, long TotalCount)> ListReportsAsync(
        ReviewReportStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var reports = dbContext.ReviewReports.AsNoTracking();

        if (status is { } wanted)
            reports = reports.Where(r => r.Status == wanted);

        var total = await reports.LongCountAsync(cancellationToken).ConfigureAwait(false);
        if (total == 0)
            return ([], 0);

        var items = await reports
            .OrderBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return ([.. items.Select(ReviewReportResponseBuilder.ToRow)], total);
    }

    public async Task<ReviewReportRow?> GetReportAsync(Guid reportId, CancellationToken cancellationToken)
    {
        var id = new ReviewReportId(reportId);
        var report = await dbContext.ReviewReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken).ConfigureAwait(false);

        return report is null ? null : ReviewReportResponseBuilder.ToRow(report);
    }

    /// <summary>A média das notas de cada pet, dentro das avaliações visíveis dos anfitriões.</summary>
    private static async Task<IReadOnlyDictionary<Guid, RatingRow>> PetRatingsAsync(
        IQueryable<Review> visible,
        List<Guid> petIds,
        CancellationToken cancellationToken)
    {
        var rows = await visible
            .SelectMany(r => r.PetRatings)
            .Where(p => petIds.Contains(p.PetId))
            .GroupBy(p => p.PetId)
            .Select(g => new { g.Key, Average = g.Average(p => (double)p.Rating), Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(
            r => r.Key,
            r => new RatingRow(Math.Round((decimal)r.Average, 2, MidpointRounding.AwayFromZero), r.Count));
    }

    private IQueryable<Review> NotDeleted() => dbContext.Reviews.AsNoTracking().Where(r => r.DeletedAt == null);
}
