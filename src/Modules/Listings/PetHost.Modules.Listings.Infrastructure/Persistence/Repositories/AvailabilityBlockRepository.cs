using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Infrastructure.Persistence.Repositories;

/// <summary>Implementação de <see cref="IAvailabilityBlockRepository"/> sobre o EF Core.</summary>
internal sealed class AvailabilityBlockRepository(ListingsDbContext dbContext) : IAvailabilityBlockRepository
{
    public Task<AvailabilityBlock?> GetAsync(ListingId listingId, AvailabilityBlockId id, CancellationToken cancellationToken) =>
        dbContext.AvailabilityBlocks.FirstOrDefaultAsync(b => b.Id == id && b.ListingId == listingId, cancellationToken);

    public async Task<IReadOnlyList<AvailabilityBlock>> ListOpenAsync(ListingId listingId, DateOnly today, CancellationToken cancellationToken) =>
        await dbContext.AvailabilityBlocks
            .AsNoTracking()
            .Where(b => b.ListingId == listingId && b.EndDate >= today)
            .OrderBy(b => b.StartDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(AvailabilityBlock block) => dbContext.AvailabilityBlocks.Add(block);

    public void Remove(AvailabilityBlock block) => dbContext.AvailabilityBlocks.Remove(block);
}
