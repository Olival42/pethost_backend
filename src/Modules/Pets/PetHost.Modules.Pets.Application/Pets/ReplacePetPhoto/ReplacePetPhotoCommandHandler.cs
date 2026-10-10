using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Modules.Pets.Domain.Errors;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Pets.Application.Pets.ReplacePetPhoto;

/// <summary>
/// Envia a imagem nova ao bucket, troca a URL da foto e apaga a imagem antiga. A posse do pet
/// e a existência da foto são conferidas <b>antes</b> do envio: foto que não é do pet dá
/// <c>404 PET_PHOTO_NOT_FOUND</c> e nada vai para o bucket. Imagem que o pet já tem (nesta
/// ou em outra foto): <c>409 PET_PHOTO_ALREADY_EXISTS</c>.
/// </summary>
public sealed class ReplacePetPhotoCommandHandler(
    IPetRepository petRepository,
    IPetsUnitOfWork unitOfWork,
    IPetKeepers petKeepers,
    IImageStorage imageStorage,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<ReplacePetPhotoCommand, PetResponse>
{
    public async Task<Result<PetResponse>> HandleAsync(ReplacePetPhotoCommand command, CancellationToken cancellationToken)
    {
        var owned = await PetAccess
            .LoadOwnedAsync(petRepository, petKeepers, command.UserId, command.Role, command.PetId, cancellationToken)
            .ConfigureAwait(false);

        if (owned.IsFailure)
            return Result<PetResponse>.FromFailure(owned);

        var pet = owned.Value!;
        var photoId = new PetPhotoId(command.PhotoId);

        if (!pet.HasPhoto(photoId))
            return Result<PetResponse>.Failure(PetsErrors.PhotoNotFound(photoId));

        return await ImageReplacement
            .ReplaceAsync(imageStorage, command.Image, ImageFolders.Pets, (image, ct) => SaveAsync(pet, photoId, image, ct), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<ImageChange<PetResponse>>> SaveAsync(
        Pet pet,
        PetPhotoId photoId,
        StoredImage image,
        CancellationToken cancellationToken)
    {
        var previous = pet.ReplacePhoto(photoId, image.Url, image.ContentHash, timeProvider.GetUtcNow());
        if (previous.IsFailure)
            return Result<ImageChange<PetResponse>>.FromFailure(previous);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await PetPhotoTrail.RecordAsync(auditTrail, pet, "photos.replaced", cancellationToken).ConfigureAwait(false);

        var response = await PetResponse.LoadAsync(pet, petKeepers, cancellationToken).ConfigureAwait(false);

        return Result<ImageChange<PetResponse>>.Success(new ImageChange<PetResponse>(response, previous.Value));
    }
}
