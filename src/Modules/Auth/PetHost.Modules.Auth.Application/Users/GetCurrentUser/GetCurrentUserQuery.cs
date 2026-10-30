using PetHost.Modules.Auth.Application.Sessions.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Auth.Application.Users.GetCurrentUser;

/// <summary>A conta dona do access token.</summary>
public sealed record GetCurrentUserQuery(Guid UserId) : IQuery<AuthenticatedUserResponse>;
