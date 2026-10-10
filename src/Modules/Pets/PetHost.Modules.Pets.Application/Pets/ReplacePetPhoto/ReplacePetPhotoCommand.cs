using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Pets.Application.Pets.ReplacePetPhoto;

/// <summary>Substitui a imagem de uma foto do pet, mantendo o id e a posição da foto.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>.</param>
/// <param name="PetId">Vem da rota.</param>
/// <param name="PhotoId">Vem da rota: a foto a substituir.</param>
/// <param name="Image">A parte <c>file</c> do multipart. Nula se não veio.</param>
public sealed record ReplacePetPhotoCommand(
    Guid UserId,
    string Role,
    Guid PetId,
    Guid PhotoId,
    ImageUpload? Image) : ICommand<PetResponse>;
