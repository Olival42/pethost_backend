using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Conversations.GetConversationById;

/// <summary>
/// A conversa vista por quem pede, com a última mensagem e as não lidas dele. Conversa de outra
/// pessoa: <c>404 CONVERSATION_NOT_FOUND</c>, como se não existisse.
/// </summary>
public sealed class GetConversationByIdQueryHandler(
    IConversationRepository conversationRepository,
    IConversationParties conversationParties,
    IConversationQueries conversationQueries,
    ConversationResponseBuilder responseBuilder) : IQueryHandler<GetConversationByIdQuery, ConversationResponse>
{
    public async Task<Result<ConversationResponse>> HandleAsync(GetConversationByIdQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var loaded = await ConversationAccess
            .LoadAsync(conversationRepository, conversationParties, query.UserId, query.Role, query.ConversationId, requireAvailable: false, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
            return Result<ConversationResponse>.FromFailure(loaded);

        var conversation = loaded.Value!.Conversation;
        var row = await conversationQueries
            .GetAsync(conversation.Id, loaded.Value.Participant.Side, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
            return Result<ConversationResponse>.Failure(ConversationsErrors.NotFound(conversation.Id));

        return Result<ConversationResponse>.Success(
            await responseBuilder.BuildAsync(row, cancellationToken).ConfigureAwait(false));
    }
}
