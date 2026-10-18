using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Reviews.Domain.Reports;

namespace PetHost.Modules.Reviews.Infrastructure.Persistence.Repositories;

/// <summary>Implementação de <see cref="IReviewReportRepository"/> sobre o EF Core.</summary>
internal sealed class ReviewReportRepository(ReviewsDbContext dbContext) : IReviewReportRepository
{
    public Task<ReviewReport?> GetByIdAsync(ReviewReportId id, CancellationToken cancellationToken) =>
        dbContext.ReviewReports.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public void Add(ReviewReport report) => dbContext.ReviewReports.Add(report);
}
