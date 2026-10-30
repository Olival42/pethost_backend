using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Application.Messages.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Messages.SendPhotoMessage;

/// <summary>
/// Envia a foto ao bucket <b>privado</b> e grava a mensagem. A posse, o perfil ativo e o outro lado
/// sem suspensão são conferidos <b>antes</b> do envio: conversa de outra pessoa é <c>404</c>,
/// destinatário suspenso é <c>403 CONVERSATION_RECIPIENT_SUSPENDED</c>, conversa bloqueada é
/// <c>422 CONVERSATION_BLOCKED</c>, e nada vai para o bucket.
/// Se a gravação falhar, a foto recém-enviada é apagada (compensação).
/// </summary>
/// <remarks>
/// A foto só abre por URL assinada (<see cref="MessagePhotoLinks"/>), para quem é da conversa. A
/// trilha e o aviso pelo hub ficam depois da gravação: uma falha neles não pode apagar a foto de uma
/// mensagem já gravada. A trava da conversa só envolve a gravação, não o envio ao bucket.
/// Com <see cref="SendPhotoMessageCommand.MessageId"/> de uma mensagem que já existe, devolve essa
/// mensagem sem enviar nada: é o que deixa a confirmação do upload direto ser repetida.
/// </remarks>
public sealed class SendPhotoMessageCommandHandler(
    IConversationRepository conversationRepository,
    IMessageRepository messageRepository,
    IConversationsUnitOfWork unitOfWork,
    IConversationParties conversationParties,
    IConversationBookings conversationBookings,
    IPrivateImageStorage imageStorage,
    MessagePhotoLinks photoLinks,
    ConversationNotifications notifications,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<SendPhotoMessageCommand, MessageResponse>
{
    public async Task<Result<MessageResponse>> HandleAsync(SendPhotoMessageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await ConversationAccess
            .LoadAsync(conversationRepository, conversationParties, command.UserId, command.Role, command.ConversationId, requireAvailable: true, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
            return Result<MessageResponse>.FromFailure(loaded);

        var (conversation, participant) = loaded.Value!;

        MessageId? messageId = command.MessageId is { } id ? new MessageId(id) : null;
        if (messageId is { } existingId)
        {
            var existing = await messageRepository.FindInConversationAsync(conversation.Id, existingId, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
                return Result<MessageResponse>.Success(await photoLinks.ToResponseAsync(existing, cancellationToken).ConfigureAwait(false));
        }

        var recipient = await ConversationAccess
            .EnsureRecipientNotSuspendedAsync(conversationParties, conversation, participant.Side, cancellationToken)
            .ConfigureAwait(false);
        if (recipient.IsFailure)
            return Result<MessageResponse>.FromFailure(recipient);

        var open = ConversationAccess.EnsureNotBlocked(conversation);
        if (open.IsFailure)
            return Result<MessageResponse>.FromFailure(open);

        var bookingId = await conversationBookings
            .GetInProgressBookingIdAsync(conversation.OwnerId, conversation.HostId, cancellationToken)
            .ConfigureAwait(false);

        var stored = await imageStorage.SaveAsync(command.Image, PrivateImageFolders.Conversations, cancellationToken).ConfigureAwait(false);
        if (stored.IsFailure)
            return Result<MessageResponse>.FromFailure(stored);

        var key = stored.Value!.Key;

        Result<Message> saved;
        try
        {
            saved = await SaveAsync(conversation, participant.Side, key, command.Body, bookingId, messageId, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // CancellationToken.None: a limpeza roda mesmo se o pedido caiu.
            await imageStorage.DeleteAsync(key, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        if (saved.IsFailure)
        {
            await imageStorage.DeleteAsync(key, CancellationToken.None).ConfigureAwait(false);
            return Result<MessageResponse>.FromFailure(saved);
        }

        var message = saved.Value!;
        var response = await photoLinks.ToResponseAsync(message, cancellationToken).ConfigureAwait(false);

        await ConversationTrail.RecordMessageSentAsync(auditTrail, message, cancellationToken).ConfigureAwait(false);
        await notifications.MessageSentAsync(conversation, participant.Side, response, cancellationToken).ConfigureAwait(false);

        return Result<MessageResponse>.Success(response);
    }

    /// <summary>Cria a mensagem com a chave que o bucket gerou e grava.</summary>
    private async Task<Result<Message>> SaveAsync(
        Conversation conversation,
        ConversationParty sentBy,
        string imageKey,
        string? caption,
        Guid? bookingId,
        MessageId? messageId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var message = Message.Photo(conversation.Id, sentBy, imageKey, caption, bookingId, now, messageId);
        if (message.IsFailure)
            return message;

        var saved = await MessageWriter
            .SaveAsync(conversationRepository, messageRepository, unitOfWork, conversation, message.Value!, cancellationToken)
            .ConfigureAwait(false);

        return saved.IsFailure ? Result<Message>.FromFailure(saved) : message;
    }
}
