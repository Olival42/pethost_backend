using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Conversations.LiftModerationBlock;

/// <summary>
/// Tira o bloqueio da moderação (procedente dado por engano, ou reconsiderado). Grava dentro da trava
/// da conversa, vai para a trilha (<c>conversation.unblocked</c> com <c>by: moderation</c>; quem fez é o
/// admin do token) e avisa os dois lados pelo hub. O bloqueio de um dos lados, se houver, continua:
/// <c>blocked</c> na resposta diz se a conversa ainda está bloqueada. Idempotente. Conversa
/// inexistente: <c>404 CONVERSATION_NOT_FOUND</c>. A denúncia continua procedente.
/// </summary>
public sealed class LiftModerationBlockCommandHandler(
    IConversationRepository conversationRepository,
    IConversationsUnitOfWork unitOfWork,
    ConversationNotifications notifications,
    IAuditTrail auditTrail,
    TimeProvider timeProvider) : ICommandHandler<LiftModerationBlockCommand, ConversationBlockResponse>
{
    public async Task<Result<ConversationBlockResponse>> HandleAsync(LiftModerationBlockCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var id = new ConversationId(command.ConversationId);
        var conversation = await conversationRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        if (conversation is null)
            return Result<ConversationBlockResponse>.Failure(ConversationsErrors.NotFound(id));

        var changed = await unitOfWork
            .RunLockedOnConversationAsync(
                id,
                async ct =>
                {
                    await conversationRepository.ReloadAsync(conversation, ct).ConfigureAwait(false);

                    if (!conversation.LiftModerationBlock(timeProvider.GetUtcNow()))
                        return Result<bool>.Success(false);

                    var saved = await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

                    return saved.IsFailure ? Result<bool>.FromFailure(saved) : Result<bool>.Success(true);
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (changed.IsFailure)
            return Result<ConversationBlockResponse>.FromFailure(changed);

        if (changed.Value)
        {
            await ConversationTrail
                .RecordBlockChangedAsync(auditTrail, AuditActions.ConversationUnblocked, conversation, ConversationTrail.ModerationActor, cancellationToken)
                .ConfigureAwait(false);

            await notifications.BlockChangedAsync(conversation, cancellationToken).ConfigureAwait(false);
        }

        return Result<ConversationBlockResponse>.Success(new ConversationBlockResponse(conversation.Id.Value, conversation.IsBlocked));
    }
}
