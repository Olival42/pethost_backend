using PetHost.Modules.Owners.Application.Owners.RegisterOwnerAccount;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Owners.Application.Owners.ReactivateOwner;

/// <summary>
/// Reativa o tutor: a conta e o perfil de tutor. Pede as credenciais do login, porque
/// conta inativa não consegue entrar para pedir isso logada. Já devolve a sessão.
/// </summary>
public sealed record ReactivateOwnerCommand(string? Email, string? Password) : ICommand<OwnerSessionResponse>;
