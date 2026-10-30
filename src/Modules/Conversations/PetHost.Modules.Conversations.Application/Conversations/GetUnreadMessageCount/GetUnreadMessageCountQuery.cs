using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Application.Conversations.GetUnreadMessageCount;

/// <summary>O contador de não lidas da conta logada, somando todas as conversas.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>.</param>
public sealed record GetUnreadMessageCountQuery(Guid UserId, string Role) : IQuery<UnreadCountResponse>;
