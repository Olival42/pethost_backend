using PetHost.Modules.Auth.Application.Sessions.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Auth.Application.Sessions.CreateSession;

/// <summary>Login: abre uma sessão.</summary>
/// <param name="Role">
/// Obrigatório porque o e-mail não é único sozinho: a mesma pessoa pode ter conta
/// <c>owner</c> e <c>host</c> com o mesmo e-mail e senhas diferentes. É a escolha
/// "Sou tutor / Sou anfitrião" da tela de entrar.
/// </param>
public sealed record CreateSessionCommand(
    string? Email,
    string? Password,
    string? Role) : ICommand<SessionResponse>;
