using PetHost.Modules.Conversations.Application.Messages.ListConversationMessages;

namespace PetHost.Modules.Conversations.Presentation.Conversations;

/// <summary>Query string de <c>GET /api/v1/conversations/{conversationId}/messages</c>.</summary>
public sealed class ConversationMessagesRequest
{
    /// <summary>
    /// Id de uma mensagem da conversa: só as anteriores a ela. Vazio = as mais recentes. Lido como
    /// texto: id mal formado volta como erro do campo.
    /// </summary>
    public string? Before { get; init; }

    /// <summary>De 1 a 100.</summary>
    public int PageSize { get; init; } = ListConversationMessagesQuery.DefaultPageSize;
}
