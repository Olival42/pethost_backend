using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Hosts.Domain.IdentityBlocks;

namespace PetHost.Modules.Hosts.Infrastructure.Persistence.Repositories;

/// <summary>Implementação de <see cref="IIdentityBlockRepository"/> sobre o EF Core.</summary>
internal sealed class IdentityBlockRepository(HostsDbContext dbContext) : IIdentityBlockRepository
{
    public Task<IdentityBlock?> FindAsync(string cpfFingerprint, CancellationToken cancellationToken) =>
        dbContext.IdentityBlocks.FirstOrDefaultAsync(b => b.CpfFingerprint == cpfFingerprint, cancellationToken);

    public void Add(IdentityBlock block) => dbContext.IdentityBlocks.Add(block);

    public void Remove(IdentityBlock block) => dbContext.IdentityBlocks.Remove(block);
}
