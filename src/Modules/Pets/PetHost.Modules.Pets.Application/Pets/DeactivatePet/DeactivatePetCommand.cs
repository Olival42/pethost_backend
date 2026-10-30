using PetHost.Modules.Pets.Application.Pets.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Pets.Application.Pets.DeactivatePet;

/// <summary>
/// Desativa um pet da conta logada (faleceu, foi doado...). Pet com histórico nunca é
/// apagado; desativado, some de quem não é o dono e pode voltar.
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>.</param>
/// <param name="PetId">Vem da rota.</param>
public sealed record DeactivatePetCommand(Guid UserId, string Role, Guid PetId) : ICommand<PetResponse>;
