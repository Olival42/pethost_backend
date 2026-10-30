using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Application.Owners.Responses;
using PetHost.Modules.Owners.Domain.Errors;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Owners.Application.Owners.GetMyOwner;

/// <summary>
/// Perfil de tutor da conta do token. <c>404</c> quer dizer "cadastro de tutor
/// incompleto": o front leva a pessoa para o passo 2.
/// </summary>
public sealed record GetMyOwnerQuery(Guid UserId) : IQuery<OwnerResponse>;

public sealed class GetMyOwnerQueryHandler(
    IOwnerRepository ownerRepository,
    IOwnerAccounts ownerAccounts) : IQueryHandler<GetMyOwnerQuery, OwnerResponse>
{
    public async Task<Result<OwnerResponse>> HandleAsync(GetMyOwnerQuery query, CancellationToken cancellationToken)
    {
        var owner = await ownerRepository.GetByUserIdAsync(query.UserId, cancellationToken).ConfigureAwait(false);

        if (owner is null)
            return Result<OwnerResponse>.Failure(OwnersErrors.ProfileNotFound);

        return Result<OwnerResponse>.Success(
            await OwnerResponse.LoadAsync(owner, ownerAccounts, cancellationToken).ConfigureAwait(false));
    }
}
