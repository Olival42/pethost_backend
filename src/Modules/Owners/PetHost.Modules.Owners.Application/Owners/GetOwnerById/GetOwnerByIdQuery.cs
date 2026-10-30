using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Application.Owners.Responses;
using PetHost.Modules.Owners.Domain.Errors;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Owners.Application.Owners.GetOwnerById;

/// <summary>Um tutor com os dados da conta. Uso do admin.</summary>
public sealed record GetOwnerByIdQuery(Guid OwnerId) : IQuery<OwnerResponse>;

public sealed class GetOwnerByIdQueryHandler(
    IOwnerRepository ownerRepository,
    IOwnerAccounts ownerAccounts,
    IAuditTrail auditTrail) : IQueryHandler<GetOwnerByIdQuery, OwnerResponse>
{
    public async Task<Result<OwnerResponse>> HandleAsync(
        GetOwnerByIdQuery query,
        CancellationToken cancellationToken)
    {
        var id = new OwnerId(query.OwnerId);
        var owner = await ownerRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (owner is null)
            return Result<OwnerResponse>.Failure(OwnersErrors.NotFound(id));

        // O admin está vendo CPF completo e endereço de outra pessoa: acesso a dado pessoal (LGPD).
        await auditTrail
            .RecordAsync(new AuditRecord(AuditActions.OwnerViewed, AuditTargets.Owner, owner.Id.Value), cancellationToken)
            .ConfigureAwait(false);

        return Result<OwnerResponse>.Success(
            await OwnerResponse.LoadAsync(owner, ownerAccounts, cancellationToken).ConfigureAwait(false));
    }
}
