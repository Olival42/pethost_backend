using PetHost.Modules.Pets.Application.Pets.AddPetPhoto;
using PetHost.Modules.Pets.Application.Pets.ReplacePetPhoto;
using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Infrastructure.Uploads;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Pets.Presentation.Uploads;

/// <summary>
/// <c>pet-photo</c>: foto do pet (<c>resourceId</c> = <c>petId</c>). Sem <c>photoId</c>, adiciona
/// (até 3); com <c>photoId</c>, substitui aquela foto. Os mesmos casos de uso do
/// <c>POST</c>/<c>PATCH /pets/{petId}/photos</c>: posse, vaga e foto repetida (por hash) iguais.
/// </summary>
internal sealed class PetPhotoUploadPurpose(
    ICommandHandler<AddPetPhotoCommand, PetResponse> addPetPhoto,
    ICommandHandler<ReplacePetPhotoCommand, PetResponse> replacePetPhoto) : IUploadPurpose
{
    public string Name => "pet-photo";

    public IReadOnlyCollection<string> Roles { get; } = [Shared.Contracts.Authorization.Roles.Owner, Shared.Contracts.Authorization.Roles.Host];

    public UploadTargetRules Target { get; } = new(Resource: "petId", AllowsPhotoId: true);

    public async Task<Result<Unit>> AuthorizeAsync(UploadPurposeContext context, CancellationToken cancellationToken) =>
        UploadResults.FromPreflight(await HandleAsync(context, ImageUpload.Preflight, cancellationToken).ConfigureAwait(false));

    public async Task<Result<object>> CompleteAsync(UploadPurposeContext context, ImageUpload image, CancellationToken cancellationToken) =>
        UploadResults.FromCompletion(await HandleAsync(context, image, cancellationToken).ConfigureAwait(false));

    private Task<Result<PetResponse>> HandleAsync(UploadPurposeContext context, ImageUpload image, CancellationToken cancellationToken) =>
        context.PhotoId is { } photoId
            ? replacePetPhoto.HandleAsync(new ReplacePetPhotoCommand(context.UserId, context.Role, context.ResourceId!.Value, photoId, image), cancellationToken)
            : addPetPhoto.HandleAsync(new AddPetPhotoCommand(context.UserId, context.Role, context.ResourceId!.Value, image), cancellationToken);
}
