using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Application.Conversations.GetConversationById;

/// <summary>Uma conversa. Só o tutor e o anfitrião dela.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>.</param>
/// <param name="ConversationId">Vem da rota.</param>
public sealed record GetConversationByIdQuery(Guid UserId, string Role, Guid ConversationId) : IQuery<ConversationResponse>;
