using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Application.Messages.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Messages.ListConversationMessages;

/// <summary>
/// Uma página de mensagens. <c>before</c> que não é um id, ou que não é mensagem desta conversa:
/// <c>400</c> no campo. Conversa de outra pessoa: <c>404 CONVERSATION_NOT_FOUND</c>. Ler não
/// marca como lida (isso é <c>PUT /read-marker</c>).
/// </summary>
public sealed class ListConversationMessagesQueryHandler(
    IConversationRepository conversationRepository,
    IConversationParties conversationParties,
    IConversationQueries conversationQueries) : IQueryHandler<ListConversationMessagesQuery, MessagePageResponse>
{
    public async Task<Result<MessagePageResponse>> HandleAsync(ListConversationMessagesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var errors = new List<Error>();

        MessageId? before = null;
        if (!string.IsNullOrWhiteSpace(query.Before))
        {
            if (Guid.TryParse(query.Before.Trim(), out var beforeId))
                before = new MessageId(beforeId);
            else
                errors.Add(ConversationsErrors.BeforeInvalid);
        }

        if (query.PageSize is < 1 or > ConversationsErrors.MaxPageSize)
            errors.Add(ConversationsErrors.PageSizeInvalid);

        if (errors.Count > 0)
            return Result<MessagePageResponse>.Failure(errors);

        var loaded = await ConversationAccess
            .LoadAsync(conversationRepository, conversationParties, query.UserId, query.Role, query.ConversationId, requireAvailable: false, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
            return Result<MessagePageResponse>.FromFailure(loaded);

        // Uma a mais que a página: se vier, há mensagens mais antigas.
        var rows = await conversationQueries
            .ListMessagesAsync(loaded.Value!.Conversation.Id, before, query.PageSize + 1, cancellationToken)
            .ConfigureAwait(false);

        if (rows is null)
            return Result<MessagePageResponse>.Failure(ConversationsErrors.BeforeInvalid);

        var items = rows.Take(query.PageSize).Select(MessageResponse.From).ToList();

        return Result<MessagePageResponse>.Success(new MessagePageResponse(items, query.PageSize, rows.Count > query.PageSize));
    }
}
