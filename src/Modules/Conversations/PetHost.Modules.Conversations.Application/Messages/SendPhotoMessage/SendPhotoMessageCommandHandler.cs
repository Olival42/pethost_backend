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
/// Envia a foto ao bucket público e grava a mensagem. A posse, o perfil ativo e o outro lado sem
/// suspensão são conferidos <b>antes</b> do envio: conversa de outra pessoa é <c>404</c>,
/// destinatário suspenso é <c>403 CONVERSATION_RECIPIENT_SUSPENDED</c>, e nada vai para o bucket.
/// Se a gravação falhar, a foto recém-enviada é apagada (<see cref="ImageReplacement"/>).
/// </summary>
/// <remarks>
/// A trilha e o aviso pelo hub ficam depois do <see cref="ImageReplacement"/>: uma falha neles não
/// pode apagar a foto de uma mensagem já gravada. A trava da conversa só envolve a gravação, não o
/// envio ao bucket: a transação não fica aberta esperando o upload.
/// </remarks>
public sealed class SendPhotoMessageCommandHandler(
    IConversationRepository conversationRepository,
    IMessageRepository messageRepository,
    IConversationsUnitOfWork unitOfWork,
    IConversationParties conversationParties,
    IConversationBookings conversationBookings,
    IImageStorage imageStorage,
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

        var recipient = await ConversationAccess
            .EnsureRecipientNotSuspendedAsync(conversationParties, conversation, participant.Side, cancellationToken)
            .ConfigureAwait(false);
        if (recipient.IsFailure)
            return Result<MessageResponse>.FromFailure(recipient);

        var bookingId = await conversationBookings
            .GetInProgressBookingIdAsync(conversation.OwnerId, conversation.HostId, cancellationToken)
            .ConfigureAwait(false);

        Message? sent = null;

        var result = await ImageReplacement
            .ReplaceAsync(
                imageStorage,
                command.Image,
                ImageFolders.Conversations,
                async (image, ct) =>
                {
                    var saved = await SaveAsync(conversation, participant.Side, image, command.Body, bookingId, ct).ConfigureAwait(false);
                    sent = saved.Value;

                    return saved.IsFailure
                        ? Result<ImageChange<MessageResponse>>.FromFailure(saved)
                        : Result<ImageChange<MessageResponse>>.Success(new ImageChange<MessageResponse>(MessageResponse.From(saved.Value!), PreviousUrl: null));
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
            return result;

        await ConversationTrail.RecordMessageSentAsync(auditTrail, sent!, cancellationToken).ConfigureAwait(false);
        await notifications.MessageSentAsync(conversation, participant.Side, result.Value!, cancellationToken).ConfigureAwait(false);

        return result;
    }

    /// <summary>Cria a mensagem com a URL que o bucket gerou e grava. Recusa ou falha: a foto é apagada.</summary>
    private async Task<Result<Message>> SaveAsync(
        Conversation conversation,
        ConversationParty sentBy,
        StoredImage image,
        string? caption,
        Guid? bookingId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var message = Message.Photo(conversation.Id, sentBy, image.Url, caption, bookingId, now);
        if (message.IsFailure)
            return message;

        var saved = await MessageWriter
            .SaveAsync(conversationRepository, messageRepository, unitOfWork, conversation, message.Value!, cancellationToken)
            .ConfigureAwait(false);

        return saved.IsFailure ? Result<Message>.FromFailure(saved) : message;
    }
}
