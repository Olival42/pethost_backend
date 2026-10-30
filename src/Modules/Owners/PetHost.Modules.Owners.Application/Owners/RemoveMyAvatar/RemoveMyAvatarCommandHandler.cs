using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Application.Owners.Responses;
using PetHost.Modules.Owners.Domain.Errors;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Owners.Application.Owners.RemoveMyAvatar;

/// <summary>Limpa a foto da conta e apaga a imagem do bucket. Idempotente.</summary>
public sealed class RemoveMyAvatarCommandHandler(
    IOwnerRepository ownerRepository,
    IOwnerAccounts ownerAccounts,
    IImageStorage imageStorage) : ICommandHandler<RemoveMyAvatarCommand, OwnerResponse>
{
    public async Task<Result<OwnerResponse>> HandleAsync(RemoveMyAvatarCommand command, CancellationToken cancellationToken)
    {
        var owner = await ownerRepository.GetByUserIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (owner is null)
            return Result<OwnerResponse>.Failure(OwnersErrors.ProfileNotFound);

        return await ImageReplacement
            .RemoveAsync(
                imageStorage,
                ct => OwnerAvatar.SetAsync(owner, null, ownerAccounts, ct),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
