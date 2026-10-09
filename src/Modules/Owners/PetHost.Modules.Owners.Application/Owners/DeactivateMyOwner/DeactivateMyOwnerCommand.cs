using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Modules.Owners.Application.Owners.DeactivateMyOwner;

/// <summary>Inativa o tutor logado: o perfil de tutor e a conta dele.</summary>
/// <param name="UserId">Vem do access token, nunca do corpo.</param>
public sealed record DeactivateMyOwnerCommand(Guid UserId) : ICommand<Unit>;
