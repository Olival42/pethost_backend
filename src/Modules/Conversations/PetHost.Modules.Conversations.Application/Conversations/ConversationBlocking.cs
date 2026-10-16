using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Conversations;

/// <summary>
/// O caminho comum de bloquear e desbloquear: quem pede é um dos lados (não exige perfil ativo —
/// bloquear é proteção), a mudança grava dentro da trava da conversa (a mesma do envio: uma mensagem
/// não passa no meio) e, quando algo mudou, vai para a trilha e para o hub. Devolve a conversa vista
/// por quem pediu.
/// </summary>
internal static class ConversationBlocking
{
    public sealed record Dependencies(
        IConversationRepository ConversationRepository,
        IConversationsUnitOfWork UnitOfWork,
        IConversationParties ConversationParties,
        IConversationQueries ConversationQueries,
        ConversationResponseBuilder ResponseBuilder,
        ConversationNotifications Notifications,
        IAuditTrail AuditTrail,
        TimeProvider TimeProvider);

    /// <param name="change">Bloqueia ou desbloqueia; devolve se mudou.</param>
    /// <param name="auditAction">A ação da trilha quando mudou.</param>
    public static async Task<Result<ConversationResponse>> ChangeAsync(
        Dependencies dependencies,
        Guid userId,
        string role,
        Guid conversationId,
        Func<Conversation, ConversationParty, DateTimeOffset, Result<bool>> change,
        string auditAction,
        CancellationToken cancellationToken)
    {
        var loaded = await ConversationAccess
            .LoadAsync(dependencies.ConversationRepository, dependencies.ConversationParties, userId, role, conversationId, requireAvailable: false, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
            return Result<ConversationResponse>.FromFailure(loaded);

        var (conversation, participant) = loaded.Value!;

        var changed = await dependencies.UnitOfWork
            .RunLockedOnConversationAsync(
                conversation.Id,
                async ct =>
                {
                    await dependencies.ConversationRepository.ReloadAsync(conversation, ct).ConfigureAwait(false);

                    var outcome = change(conversation, participant.Side, dependencies.TimeProvider.GetUtcNow());
                    if (outcome.IsFailure || !outcome.Value)
                        return outcome;

                    var saved = await dependencies.UnitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

                    return saved.IsFailure ? Result<bool>.FromFailure(saved) : outcome;
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (changed.IsFailure)
            return Result<ConversationResponse>.FromFailure(changed);

        if (changed.Value)
        {
            await ConversationTrail
                .RecordBlockChangedAsync(dependencies.AuditTrail, auditAction, conversation, ConversationPartyValues.ToWire(participant.Side), cancellationToken)
                .ConfigureAwait(false);

            await dependencies.Notifications.BlockChangedAsync(conversation, cancellationToken).ConfigureAwait(false);
        }

        var row = await dependencies.ConversationQueries
            .GetAsync(conversation.Id, participant.Side, cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
            return Result<ConversationResponse>.Failure(ConversationsErrors.NotFound(conversation.Id));

        return Result<ConversationResponse>.Success(
            await dependencies.ResponseBuilder.BuildAsync(row, cancellationToken).ConfigureAwait(false));
    }
}
