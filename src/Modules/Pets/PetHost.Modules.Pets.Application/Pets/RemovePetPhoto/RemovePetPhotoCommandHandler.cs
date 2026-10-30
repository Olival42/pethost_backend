using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Pets.Application.Pets.RemovePetPhoto;

/// <summary>
/// Tira a foto do pet e apaga a imagem do bucket. Só o dono; foto que não é do pet:
/// <c>404 PET_PHOTO_NOT_FOUND</c>.
/// </summary>
public sealed class RemovePetPhotoCommandHandler(
    IPetRepository petRepository,
    IPetsUnitOfWork unitOfWork,
    IPetKeepers petKeepers,
    IImageStorage imageStorage,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<RemovePetPhotoCommand, PetResponse>
{
    public async Task<Result<PetResponse>> HandleAsync(RemovePetPhotoCommand command, CancellationToken cancellationToken)
    {
        var owned = await PetAccess
            .LoadOwnedAsync(petRepository, petKeepers, command.UserId, command.Role, command.PetId, cancellationToken)
            .ConfigureAwait(false);

        if (owned.IsFailure)
            return Result<PetResponse>.FromFailure(owned);

        var pet = owned.Value!;

        return await ImageReplacement
            .RemoveAsync(imageStorage, ct => RemoveAsync(pet, new PetPhotoId(command.PhotoId), ct), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<ImageChange<PetResponse>>> RemoveAsync(Pet pet, PetPhotoId photoId, CancellationToken cancellationToken)
    {
        var removed = pet.RemovePhoto(photoId, timeProvider.GetUtcNow());
        if (removed.IsFailure)
            return Result<ImageChange<PetResponse>>.FromFailure(removed);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await PetPhotoTrail.RecordAsync(auditTrail, pet, "photos.removed", cancellationToken).ConfigureAwait(false);

        var response = await PetResponse.LoadAsync(pet, petKeepers, cancellationToken).ConfigureAwait(false);

        return Result<ImageChange<PetResponse>>.Success(new ImageChange<PetResponse>(response, removed.Value!.Url));
    }
}
