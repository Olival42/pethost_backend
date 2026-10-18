using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;

namespace PetHost.Modules.Reviews.Infrastructure.Persistence.Repositories;

/// <summary>Implementação de <see cref="IReviewReportRepository"/> sobre o EF Core.</summary>
internal sealed class ReviewReportRepository(ReviewsDbContext dbContext) : IReviewReportRepository
{
    public Task<ReviewReport?> GetByIdAsync(ReviewReportId id, CancellationToken cancellationToken) =>
        dbContext.ReviewReports.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ReviewReport>> ListPendingForReviewAsync(ReviewId reviewId, CancellationToken cancellationToken) =>
        await dbContext.ReviewReports
            .Where(r => r.ReviewId == reviewId && r.Status == ReviewReportStatus.Pending)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(ReviewReport report) => dbContext.ReviewReports.Add(report);
}
