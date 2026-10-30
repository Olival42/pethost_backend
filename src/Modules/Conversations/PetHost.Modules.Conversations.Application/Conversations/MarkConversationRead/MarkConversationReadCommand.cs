using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Application.Conversations.MarkConversationRead;

/// <summary>Quem abriu a conversa leu tudo o que recebeu nela. Devolve o contador novo.</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>.</param>
/// <param name="ConversationId">Vem da rota.</param>
public sealed record MarkConversationReadCommand(Guid UserId, string Role, Guid ConversationId) : ICommand<UnreadCountResponse>;
