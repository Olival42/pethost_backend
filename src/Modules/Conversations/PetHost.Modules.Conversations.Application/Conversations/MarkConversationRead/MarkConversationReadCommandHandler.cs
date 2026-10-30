using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Conversations.MarkConversationRead;

/// <summary>
/// Marca como lidas as mensagens que quem pede recebeu na conversa e devolve o total de não lidas
/// dele. Idempotente: chamar de novo não muda nada. Quando algo foi marcado, as outras abas e
/// aparelhos da conta recebem o contador novo pelo hub.
/// </summary>
/// <remarks>Ler não exige perfil ativo: inativo e suspenso ainda veem o histórico.</remarks>
public sealed class MarkConversationReadCommandHandler(
    IConversationRepository conversationRepository,
    IMessageRepository messageRepository,
    IConversationParties conversationParties,
    IConversationQueries conversationQueries,
    IConversationRealtime conversationRealtime,
    TimeProvider timeProvider) : ICommandHandler<MarkConversationReadCommand, UnreadCountResponse>
{
    public async Task<Result<UnreadCountResponse>> HandleAsync(MarkConversationReadCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var loaded = await ConversationAccess
            .LoadAsync(conversationRepository, conversationParties, command.UserId, command.Role, command.ConversationId, requireAvailable: false, cancellationToken)
            .ConfigureAwait(false);

        if (loaded.IsFailure)
            return Result<UnreadCountResponse>.FromFailure(loaded);

        var participant = loaded.Value!.Participant;

        var marked = await messageRepository
            .MarkReceivedAsReadAsync(loaded.Value.Conversation.Id, participant.Side, timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        var total = await conversationQueries.CountUnreadAsync(participant, cancellationToken).ConfigureAwait(false);

        if (marked > 0)
            await conversationRealtime.UnreadCountChangedAsync(command.UserId, total, cancellationToken).ConfigureAwait(false);

        return Result<UnreadCountResponse>.Success(new UnreadCountResponse(total));
    }
}
