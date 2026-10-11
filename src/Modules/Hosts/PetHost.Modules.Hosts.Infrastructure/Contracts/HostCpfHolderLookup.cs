using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Modules.Hosts.Infrastructure.Persistence;
using PetHost.Shared.Contracts.Identity;

namespace PetHost.Modules.Hosts.Infrastructure.Contracts;

/// <summary>
/// A parte do Hosts na regra do CPF entre papéis: as contas com este CPF num anfitrião
/// (pessoa física ou representante de empresa) ou numa solicitação ainda aberta.
/// </summary>
internal sealed class HostCpfHolderLookup(HostsDbContext dbContext) : ICpfHolderLookup
{
    public async Task<IReadOnlyCollection<Guid>> FindAccountIdsAsync(string cpf, CancellationToken cancellationToken)
    {
        var parsed = Cpf.Create(cpf);
        if (parsed.IsFailure)
            return [];

        var value = parsed.Value!;

        var hosts = await dbContext.Hosts
            .AsNoTracking()
            .Where(h => h.Cpf == value && h.IdentityReleasedAt == null)
            .Select(h => h.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var applications = await dbContext.HostApplications
            .AsNoTracking()
            .Where(a => a.Cpf == value && (a.Status == HostApplicationStatus.Draft || a.Status == HostApplicationStatus.Submitted))
            .Select(a => a.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. hosts.Union(applications)];
    }
}
