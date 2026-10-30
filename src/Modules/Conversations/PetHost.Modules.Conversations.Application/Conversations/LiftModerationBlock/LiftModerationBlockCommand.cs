using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Application.Conversations.LiftModerationBlock;

/// <summary>O admin tira o bloqueio da moderação de uma conversa.</summary>
/// <param name="AdminUserId">Vem do access token.</param>
/// <param name="ConversationId">Vem da rota.</param>
public sealed record LiftModerationBlockCommand(Guid AdminUserId, Guid ConversationId) : ICommand<ConversationBlockResponse>;
