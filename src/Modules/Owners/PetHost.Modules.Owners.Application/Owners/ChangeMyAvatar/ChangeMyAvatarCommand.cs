using PetHost.Modules.Owners.Application.Owners.Responses;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Owners.Application.Owners.ChangeMyAvatar;

/// <summary>Troca a foto de perfil do tutor logado pela imagem enviada.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Image">A parte <c>file</c> do multipart. Nula se não veio.</param>
public sealed record ChangeMyAvatarCommand(Guid UserId, ImageUpload? Image) : ICommand<OwnerResponse>;
