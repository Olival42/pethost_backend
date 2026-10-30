using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Application.Conversations.UnblockConversation;

/// <summary>Quem bloqueou a conversa a desbloqueia.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c> — o lado de quem desbloqueia.</param>
/// <param name="ConversationId">Vem da rota.</param>
public sealed record UnblockConversationCommand(Guid UserId, string Role, Guid ConversationId) : ICommand<ConversationResponse>;
