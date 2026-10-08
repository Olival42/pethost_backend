using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Modules.Auth.Application.Sessions.RevokeSession;

/// <summary>Logout: invalida o refresh token apresentado.</summary>
public sealed record RevokeSessionCommand(string? RefreshToken) : ICommand<Unit>;
