using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Modules.Owners.Infrastructure.Persistence;
using PetHost.Shared.Contracts.Identity;

namespace PetHost.Modules.Owners.Infrastructure.Accounts;

/// <summary>A parte do Owners na regra do CPF entre papéis: as contas de tutor com este CPF.</summary>
internal sealed class OwnerCpfHolderLookup(OwnersDbContext dbContext) : ICpfHolderLookup
{
    public async Task<IReadOnlyCollection<Guid>> FindAccountIdsAsync(string cpf, CancellationToken cancellationToken)
    {
        var parsed = Cpf.Create(cpf);
        if (parsed.IsFailure)
            return [];

        var value = parsed.Value!;

        return await dbContext.Owners
            .AsNoTracking()
            .Where(o => o.Cpf == value)
            .Select(o => o.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
