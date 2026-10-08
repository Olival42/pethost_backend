using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Modules.Auth.Application.Passwords.ResetPassword;

/// <summary>Troca a senha usando o token recebido por e-mail.</summary>
public sealed record ResetPasswordCommand(
    string? Token,
    string? NewPassword) : ICommand<Unit>;
