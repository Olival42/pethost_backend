using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Pets.Application.Pets.ReactivatePet;

/// <summary>Reativa um pet desativado da conta logada.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>.</param>
/// <param name="PetId">Vem da rota.</param>
public sealed record ReactivatePetCommand(Guid UserId, string Role, Guid PetId) : ICommand<PetResponse>;
