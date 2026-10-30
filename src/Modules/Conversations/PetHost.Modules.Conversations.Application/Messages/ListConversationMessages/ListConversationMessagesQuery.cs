using PetHost.Modules.Conversations.Application.Messages.Responses;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Conversations.Application.Messages.ListConversationMessages;

/// <summary>
/// As mensagens da conversa, da mais nova para a mais antiga. Paginação por cursor (exceção ao
/// <c>page</c>/<c>pageSize</c> do §13): mensagem nova chegando no meio não repete nem pula nada.
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>.</param>
/// <param name="ConversationId">Vem da rota.</param>
/// <param name="Before">Id de uma mensagem da conversa: só as anteriores a ela. Vazio = as mais recentes.</param>
/// <param name="PageSize">De 1 a 100.</param>
public sealed record ListConversationMessagesQuery(
    Guid UserId,
    string Role,
    Guid ConversationId,
    string? Before = null,
    int PageSize = ListConversationMessagesQuery.DefaultPageSize) : IQuery<MessagePageResponse>
{
    /// <summary>Página padrão de mensagens.</summary>
    public const int DefaultPageSize = 50;
}
