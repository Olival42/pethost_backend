using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Messages.GetMessagePhoto;

/// <summary>Um link novo para a foto de uma mensagem (o da listagem venceu).</summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c> ou <c>host</c>.</param>
public sealed record GetMessagePhotoQuery(Guid UserId, string Role, Guid ConversationId, Guid MessageId) : IQuery<MessagePhotoResponse>;

/// <summary>Link da foto de uma mensagem.</summary>
/// <param name="Url">URL assinada do bucket privado (ou a URL pública, nas fotos antigas).</param>
/// <param name="ExpiresAt">Vencimento da URL, em Unix time (segundos, UTC). Omitido nas fotos antigas.</param>
public sealed record MessagePhotoResponse(Guid MessageId, string Url, long? ExpiresAt);

/// <summary>
/// Só quem é da conversa (mesma checagem da listagem): conversa de outra pessoa é <c>404</c>. Mensagem
/// que não é desta conversa: <c>404 CONVERSATION_MESSAGE_NOT_FOUND</c>; sem foto, ou foto já apagada
/// do bucket: <c>404 CONVERSATION_MESSAGE_PHOTO_NOT_FOUND</c>.
/// </summary>
public sealed class GetMessagePhotoQueryHandler(
    IConversationRepository conversationRepository,
    IConversationParties conversationParties,
    IMessageRepository messageRepository,
    IPrivateImageStorage storage) : IQueryHandler<GetMessagePhotoQuery, MessagePhotoResponse>
{
    public async Task<Result<MessagePhotoResponse>> HandleAsync(GetMessagePhotoQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var loaded = await ConversationAccess
            .LoadAsync(conversationRepository, conversationParties, query.UserId, query.Role, query.ConversationId, requireAvailable: false, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
            return Result<MessagePhotoResponse>.FromFailure(loaded);

        var id = new MessageId(query.MessageId);
        var message = await messageRepository
            .FindInConversationAsync(loaded.Value!.Conversation.Id, id, cancellationToken)
            .ConfigureAwait(false);

        if (message is null)
            return Result<MessagePhotoResponse>.Failure(ConversationsErrors.MessageNotFound(id));

        if (message.ImageKey is { } key)
        {
            var signed = await storage.GetSignedUrlAsync(key, cancellationToken).ConfigureAwait(false);

            return signed is null
                ? Result<MessagePhotoResponse>.Failure(ConversationsErrors.MessagePhotoNotFound(id))
                : Result<MessagePhotoResponse>.Success(new MessagePhotoResponse(id.Value, signed.Url, signed.ExpiresAt.ToUnixTimeSeconds()));
        }

        return message.ImageUrl is { } url
            ? Result<MessagePhotoResponse>.Success(new MessagePhotoResponse(id.Value, url, ExpiresAt: null))
            : Result<MessagePhotoResponse>.Failure(ConversationsErrors.MessagePhotoNotFound(id));
    }
}
