using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Modules.Auth.Application.Passwords.ForgotPassword;

/// <summary>"Esqueci a senha": pede o envio do token de troca por e-mail.</summary>
/// <param name="Role">
/// Obrigatório pelo mesmo motivo do login: o e-mail sozinho não identifica a conta,
/// porque a mesma pessoa pode ter conta <c>owner</c> e <c>host</c>.
/// </param>
public sealed record ForgotPasswordCommand(
    string? Email,
    string? Role) : ICommand<Unit>;
