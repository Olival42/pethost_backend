using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Messages;

/// <summary>
/// Grava a mensagem nova e avança a última atividade da conversa dentro da trava da conversa. Dois
/// envios ao mesmo tempo (os dois lados escrevendo juntos) gravam em fila, e cada um aplica a
/// mensagem sobre a conversa relida do banco. Sem a trava, quem gravava por último podia voltar
/// <c>last_message_at</c> no tempo, e a lista e a retenção olhavam um horário antigo. O bloqueio
/// grava na mesma trava: a conversa relida que está bloqueada não recebe a mensagem.
/// </summary>
internal static class MessageWriter
{
    public static Task<Result> SaveAsync(
        IConversationRepository conversationRepository,
        IMessageRepository messageRepository,
        IConversationsUnitOfWork unitOfWork,
        Conversation conversation,
        Message message,
        CancellationToken cancellationToken) =>
        unitOfWork.RunLockedOnConversationAsync(
            conversation.Id,
            async ct =>
            {
                await conversationRepository.ReloadAsync(conversation, ct).ConfigureAwait(false);

                // Bloqueada depois da primeira conferência: o bloqueio grava na mesma trava.
                var open = ConversationAccess.EnsureNotBlocked(conversation);
                if (open.IsFailure)
                    return open;

                conversation.RecordMessage(message.CreatedAt);
                messageRepository.Add(message);

                return await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
            },
            cancellationToken);
}
