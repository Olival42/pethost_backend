using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Modules.Pets.Domain.Errors;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Pets.Application.Pets.AddPetPhoto;

/// <summary>
/// Envia a imagem ao bucket e põe a foto na primeira vaga livre do pet. A posse do pet e a
/// vaga são conferidas <b>antes</b> do envio: sem vaga, <c>422 PET_PHOTO_LIMIT_REACHED</c>
/// e nada vai para o bucket. Imagem que o pet já tem: <c>409 PET_PHOTO_ALREADY_EXISTS</c>.
/// </summary>
public sealed class AddPetPhotoCommandHandler(
    IPetRepository petRepository,
    IPetsUnitOfWork unitOfWork,
    IPetKeepers petKeepers,
    IImageStorage imageStorage,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<AddPetPhotoCommand, PetResponse>
{
    public async Task<Result<PetResponse>> HandleAsync(AddPetPhotoCommand command, CancellationToken cancellationToken)
    {
        var owned = await PetAccess
            .LoadOwnedAsync(petRepository, petKeepers, command.UserId, command.Role, command.PetId, cancellationToken)
            .ConfigureAwait(false);

        if (owned.IsFailure)
            return Result<PetResponse>.FromFailure(owned);

        var pet = owned.Value!;

        if (!pet.CanAddPhoto)
            return Result<PetResponse>.Failure(PetsErrors.PhotoLimitReached);

        return await ImageReplacement
            .ReplaceAsync(imageStorage, command.Image, ImageFolders.Pets, (image, ct) => SaveAsync(pet, image, ct), cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<Result<ImageChange<PetResponse>>> SaveAsync(Pet pet, StoredImage image, CancellationToken cancellationToken)
    {
        // Imagem repetida só se sabe depois do envio (é o hash dos bytes): a recusa apaga a
        // imagem recém-enviada (ImageReplacement).
        var added = pet.AddPhoto(image.Url, image.ContentHash, timeProvider.GetUtcNow());
        if (added.IsFailure)
            return Result<ImageChange<PetResponse>>.FromFailure(added);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await PetPhotoTrail.RecordAsync(auditTrail, pet, "photos.added", cancellationToken).ConfigureAwait(false);

        var response = await PetResponse.LoadAsync(pet, petKeepers, cancellationToken).ConfigureAwait(false);

        return Result<ImageChange<PetResponse>>.Success(new ImageChange<PetResponse>(response, PreviousUrl: null));
    }
}
