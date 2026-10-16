namespace PetHost.Modules.Conversations.Application.Conversations.Responses;

/// <summary>
/// A conversa foi bloqueada ou desbloqueada (evento do hub). Não diz quem bloqueou: o app recarrega a
/// conversa pelo REST (<c>blockedByMe</c>) se precisar.
/// </summary>
public sealed record ConversationBlockResponse(Guid ConversationId, bool Blocked);
