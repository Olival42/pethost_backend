using PetHost.Modules.Auth.Application.Sessions.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Auth.Application.Users.ChangePassword;

/// <summary>Troca a senha da conta logada, confirmando a senha atual.</summary>
/// <param name="UserId">Vem do access token, nunca do corpo.</param>
public sealed record ChangePasswordCommand(
    Guid UserId,
    string? CurrentPassword,
    string? NewPassword) : ICommand<SessionResponse>;
