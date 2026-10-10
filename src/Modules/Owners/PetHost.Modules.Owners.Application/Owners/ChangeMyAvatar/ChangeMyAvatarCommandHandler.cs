using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Application.Owners.Responses;
using PetHost.Modules.Owners.Domain.Errors;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Owners.Application.Owners.ChangeMyAvatar;

/// <summary>
/// Envia a imagem para o bucket, grava a URL na conta (<c>user.avatarUrl</c>, no Auth) e
/// apaga a foto anterior. A trilha de auditoria fica com o Auth (<c>account.profile_updated</c>).
/// </summary>
public sealed class ChangeMyAvatarCommandHandler(
    IOwnerRepository ownerRepository,
    IOwnerAccounts ownerAccounts,
    IImageStorage imageStorage) : ICommandHandler<ChangeMyAvatarCommand, OwnerResponse>
{
    public async Task<Result<OwnerResponse>> HandleAsync(ChangeMyAvatarCommand command, CancellationToken cancellationToken)
    {
        var owner = await ownerRepository.GetByUserIdAsync(command.UserId, cancellationToken).ConfigureAwait(false);
        if (owner is null)
            return Result<OwnerResponse>.Failure(OwnersErrors.ProfileNotFound);

        return await ImageReplacement
            .ReplaceAsync(
                imageStorage,
                command.Image,
                ImageFolders.Avatars,
                (image, ct) => OwnerAvatar.SetAsync(owner, image.Url, ownerAccounts, ct),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
