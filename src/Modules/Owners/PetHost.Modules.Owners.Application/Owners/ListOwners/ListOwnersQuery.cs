using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Application.Owners.Responses;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Owners.Application.Owners.ListOwners;

/// <summary>Todos os tutores com os dados da conta. Uso do admin; sem paginação nem filtro no MVP.</summary>
public sealed record ListOwnersQuery : IQuery<IReadOnlyList<OwnerResponse>>;

public sealed class ListOwnersQueryHandler(
    IOwnerRepository ownerRepository,
    IOwnerAccounts ownerAccounts,
    IAuditTrail auditTrail) : IQueryHandler<ListOwnersQuery, IReadOnlyList<OwnerResponse>>
{
    public async Task<Result<IReadOnlyList<OwnerResponse>>> HandleAsync(
        ListOwnersQuery query,
        CancellationToken cancellationToken)
    {
        var owners = await ownerRepository.ListAsync(cancellationToken).ConfigureAwait(false);

        // Uma consulta ao Auth para a lista inteira, não uma por tutor.
        var accounts = await ownerAccounts
            .GetByIdsAsync([.. owners.Select(o => o.UserId)], cancellationToken)
            .ConfigureAwait(false);

        var accountsById = accounts.ToDictionary(a => a.Id);

        IReadOnlyList<OwnerResponse> response =
        [
            .. owners.Select(o => OwnerResponse.From(o, accountsById.GetValueOrDefault(o.UserId))),
        ];

        // A lista mostra CPF completo de todos: acesso a dado pessoal em massa (LGPD).
        await auditTrail
            .RecordAsync(
                new AuditRecord(
                    AuditActions.OwnersListed,
                    AuditTargets.Owner,
                    TargetId: null,
                    Details: new Dictionary<string, string?> { ["count"] = response.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) }),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<IReadOnlyList<OwnerResponse>>.Success(response);
    }
}
