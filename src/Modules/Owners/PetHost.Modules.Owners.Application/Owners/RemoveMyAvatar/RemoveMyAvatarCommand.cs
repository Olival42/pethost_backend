using PetHost.Modules.Owners.Application.Owners.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Owners.Application.Owners.RemoveMyAvatar;

/// <summary>Tira a foto de perfil do tutor logado. Sem foto: nada muda.</summary>
/// <param name="UserId">Vem do access token.</param>
public sealed record RemoveMyAvatarCommand(Guid UserId) : ICommand<OwnerResponse>;
