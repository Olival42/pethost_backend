using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;

namespace PetHost.Modules.Hosts.Infrastructure.Persistence.Repositories;

/// <summary>Implementação de <see cref="IHostApplicationRepository"/> sobre o EF Core.</summary>
internal sealed class HostApplicationRepository(HostsDbContext dbContext) : IHostApplicationRepository
{
    private IQueryable<HostApplication> WithDocuments => dbContext.HostApplications.Include(a => a.Documents);

    public Task<HostApplication?> GetByIdAsync(HostApplicationId id, CancellationToken cancellationToken) =>
        WithDocuments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<HostApplication>> ListByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        await WithDocuments
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<bool> ExistsOpenWithCpfAsync(Cpf cpf, Guid exceptUserId, CancellationToken cancellationToken) =>
        dbContext.HostApplications
            .AsNoTracking()
            .AnyAsync(
                a => a.Cpf == cpf
                    && a.UserId != exceptUserId
                    && (a.Status == HostApplicationStatus.Draft || a.Status == HostApplicationStatus.Submitted),
                cancellationToken);

    public Task<bool> ExistsOpenWithCnpjAsync(Cnpj cnpj, Guid exceptUserId, CancellationToken cancellationToken) =>
        dbContext.HostApplications
            .AsNoTracking()
            .AnyAsync(
                a => a.Cnpj == cnpj
                    && a.UserId != exceptUserId
                    && (a.Status == HostApplicationStatus.Draft || a.Status == HostApplicationStatus.Submitted),
                cancellationToken);

    public async Task<IReadOnlyList<HostApplication>> ListByStatusAsync(HostApplicationStatus status, CancellationToken cancellationToken)
    {
        var query = dbContext.HostApplications
            .AsNoTracking()
            .Include(a => a.Documents)
            .Where(a => a.Status == status);

        query = status == HostApplicationStatus.Submitted
            ? query.OrderBy(a => a.SubmittedAt)
            : query.OrderByDescending(a => a.UpdatedAt);

        return await query.ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HostApplication>> ListDraftsCreatedBeforeAsync(DateTimeOffset createdBefore, CancellationToken cancellationToken) =>
        await WithDocuments
            .Where(a => a.Status == HostApplicationStatus.Draft && a.CreatedAt < createdBefore)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<HostApplication>> ListWithDocumentsClosedBeforeAsync(DateTimeOffset closedBefore, CancellationToken cancellationToken) =>
        await WithDocuments
            .Where(a => a.ClosedAt != null && a.ClosedAt <= closedBefore && a.DocumentsPurgedAt == null && a.Documents.Any())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(HostApplication application) => dbContext.HostApplications.Add(application);

    public void Remove(HostApplication application) => dbContext.HostApplications.Remove(application);
}
