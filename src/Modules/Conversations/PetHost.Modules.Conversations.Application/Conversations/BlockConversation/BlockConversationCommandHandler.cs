using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Conversations.BlockConversation;

/// <summary>
/// Bloqueia a conversa pelo lado de quem pede. Idempotente: bloquear de novo devolve a mesma
/// conversa, sem trilha nem aviso. Já bloqueada pelo outro lado: <c>422 CONVERSATION_ALREADY_BLOCKED</c>.
/// Não exige perfil ativo: bloquear é proteção. Os dois lados recebem o aviso pelo hub.
/// </summary>
public sealed class BlockConversationCommandHandler(
    IConversationRepository conversationRepository,
    IConversationsUnitOfWork unitOfWork,
    IConversationParties conversationParties,
    IConversationQueries conversationQueries,
    ConversationResponseBuilder responseBuilder,
    ConversationNotifications notifications,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<BlockConversationCommand, ConversationResponse>
{
    public Task<Result<ConversationResponse>> HandleAsync(BlockConversationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ConversationBlocking.ChangeAsync(
            new ConversationBlocking.Dependencies(
                conversationRepository, unitOfWork, conversationParties, conversationQueries, responseBuilder, notifications, auditTrail, timeProvider),
            command.UserId,
            command.Role,
            command.ConversationId,
            (conversation, side, now) => conversation.Block(side, now),
            AuditActions.ConversationBlocked,
            cancellationToken);
    }
}
