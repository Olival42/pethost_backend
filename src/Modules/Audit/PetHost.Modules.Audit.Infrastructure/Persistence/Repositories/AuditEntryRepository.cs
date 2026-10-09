using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Audit.Domain.Entries;

namespace PetHost.Modules.Audit.Infrastructure.Persistence.Repositories;

/// <summary>Implementação de <see cref="IAuditEntryRepository"/> sobre o EF Core.</summary>
internal sealed class AuditEntryRepository(AuditDbContext dbContext) : IAuditEntryRepository
{
    public async Task<(IReadOnlyList<AuditEntry> Items, long TotalCount)> SearchAsync(
        AuditEntryFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var query = dbContext.Entries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Action))
            query = query.Where(e => e.Action == filter.Action);
        if (!string.IsNullOrWhiteSpace(filter.TargetType))
            query = query.Where(e => e.TargetType == filter.TargetType);
        if (filter.TargetId is { } targetId)
            query = query.Where(e => e.TargetId == targetId);
        if (filter.ActorId is { } actorId)
            query = query.Where(e => e.ActorId == actorId);
        if (filter.From is { } from)
            query = query.Where(e => e.OccurredAt >= from);
        if (filter.To is { } to)
            query = query.Where(e => e.OccurredAt <= to);

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .OrderByDescending(e => e.OccurredAt)
            .ThenByDescending(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (items, total);
    }
}
