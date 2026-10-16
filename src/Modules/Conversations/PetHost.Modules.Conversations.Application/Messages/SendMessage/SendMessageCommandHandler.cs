using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Application.Messages.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Messages.SendMessage;

/// <summary>
/// Grava a mensagem de texto e avisa os dois lados pelo hub. Só o tutor e o anfitrião da
/// conversa, ativos e sem suspensão (<c>403 CONVERSATION_OWNER_DEACTIVATED</c> /
/// <c>CONVERSATION_HOST_DEACTIVATED</c>), e o outro lado não pode estar suspenso
/// (<c>403 CONVERSATION_RECIPIENT_SUSPENDED</c>). Com a dupla numa reserva em andamento, a mensagem
/// leva o id dela (histórico da reserva).
/// </summary>
public sealed class SendMessageCommandHandler(
    IConversationRepository conversationRepository,
    IMessageRepository messageRepository,
    IConversationsUnitOfWork unitOfWork,
    IConversationParties conversationParties,
    IConversationBookings conversationBookings,
    ConversationNotifications notifications,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<SendMessageCommand, MessageResponse>
{
    public async Task<Result<MessageResponse>> HandleAsync(SendMessageCommand command, CancellationToken cancellationToken)
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

        var now = timeProvider.GetUtcNow();
        var message = Message.Text(conversation.Id, participant.Side, command.Body, bookingId, now);
        if (message.IsFailure)
            return Result<MessageResponse>.FromFailure(message);

        var saved = await MessageWriter
            .SaveAsync(conversationRepository, messageRepository, unitOfWork, conversation, message.Value!, cancellationToken)
            .ConfigureAwait(false);
        if (saved.IsFailure)
            return Result<MessageResponse>.FromFailure(saved);

        await ConversationTrail.RecordMessageSentAsync(auditTrail, message.Value!, cancellationToken).ConfigureAwait(false);

        var response = MessageResponse.From(message.Value!);
        await notifications.MessageSentAsync(conversation, participant.Side, response, cancellationToken).ConfigureAwait(false);

        return Result<MessageResponse>.Success(response);
    }
}
