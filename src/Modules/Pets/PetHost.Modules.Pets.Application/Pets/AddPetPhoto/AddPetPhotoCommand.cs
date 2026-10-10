using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Pets.Application.Pets.AddPetPhoto;

/// <summary>Adiciona uma foto ao pet da conta logada (até 3).</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>.</param>
/// <param name="PetId">Vem da rota.</param>
/// <param name="Image">A parte <c>file</c> do multipart. Nula se não veio.</param>
public sealed record AddPetPhotoCommand(Guid UserId, string Role, Guid PetId, ImageUpload? Image) : ICommand<PetResponse>;
