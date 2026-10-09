using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Modules.Auth.Application.Users.SuspendAccount;

/// <summary>
/// Suspende uma conta (ação do admin). Sem rota no Auth: o módulo do papel chama pelo
/// contrato <c>IAccountStatusManager</c>, junto com o perfil dele.
/// </summary>
/// <param name="AdminId">Quem suspendeu — vem do token do admin.</param>
public sealed record SuspendAccountCommand(Guid UserId, string? Reason, Guid AdminId) : ICommand<Unit>;

/// <summary>Tira a suspensão de uma conta (ação do admin).</summary>
public sealed record LiftAccountSuspensionCommand(Guid UserId) : ICommand<Unit>;
