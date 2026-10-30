using PetHost.Modules.Auth.Application.Sessions.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Auth.Application.Sessions.SwitchSession;

/// <summary>
/// Troca para a outra conta da mesma pessoa (mesmo e-mail): de tutor para anfitrião
/// ou o contrário.
/// </summary>
/// <remarks>
/// Pede a senha da conta de destino porque o e-mail ainda não é verificado: sem isso,
/// quem criasse uma conta com o e-mail de outra pessoa poderia entrar na conta dela.
/// </remarks>
/// <param name="UserId">Vem do access token, nunca do corpo.</param>
/// <param name="Role">Papel da conta de destino: <c>owner</c> ou <c>host</c>.</param>
public sealed record SwitchSessionCommand(
    Guid UserId,
    string? Role,
    string? Password) : ICommand<SessionResponse>;
