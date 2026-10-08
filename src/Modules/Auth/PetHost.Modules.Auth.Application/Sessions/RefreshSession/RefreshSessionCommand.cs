using PetHost.Modules.Auth.Application.Sessions.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Auth.Application.Sessions.RefreshSession;

/// <summary>Troca um refresh token válido por um par novo de tokens.</summary>
public sealed record RefreshSessionCommand(string? RefreshToken) : ICommand<SessionResponse>;
