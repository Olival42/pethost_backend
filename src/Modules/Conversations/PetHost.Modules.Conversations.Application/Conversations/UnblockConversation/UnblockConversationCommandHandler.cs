using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Conversations.UnblockConversation;

/// <summary>
/// Desbloqueia a conversa. Só quem bloqueou desbloqueia (<c>403 CONVERSATION_UNBLOCK_FORBIDDEN</c>).
/// Idempotente: conversa já liberada volta como está, sem trilha nem aviso. Os dois lados recebem o
/// aviso pelo hub.
/// </summary>
public sealed class UnblockConversationCommandHandler(
    IConversationRepository conversationRepository,
    IConversationsUnitOfWork unitOfWork,
    IConversationParties conversationParties,
    IConversationQueries conversationQueries,
    ConversationResponseBuilder responseBuilder,
    ConversationNotifications notifications,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<UnblockConversationCommand, ConversationResponse>
{
    public Task<Result<ConversationResponse>> HandleAsync(UnblockConversationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ConversationBlocking.ChangeAsync(
            new ConversationBlocking.Dependencies(
                conversationRepository, unitOfWork, conversationParties, conversationQueries, responseBuilder, notifications, auditTrail, timeProvider),
            command.UserId,
            command.Role,
            command.ConversationId,
            (conversation, side, now) => conversation.Unblock(side, now),
            AuditActions.ConversationUnblocked,
            cancellationToken);
    }
}
