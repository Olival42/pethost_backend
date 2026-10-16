using PetHost.Modules.Conversations.Domain.Conversations;

namespace PetHost.Modules.Conversations.Domain.Messages;

/// <summary>Acesso a <see cref="Message"/>. A Application só conhece esta interface (§11).</summary>
public interface IMessageRepository
{
    /// <summary>
    /// Marca como lidas, em <paramref name="now"/>, as mensagens da conversa que
    /// <paramref name="reader"/> recebeu (as enviadas pelo outro lado) e ainda não tinha lido.
    /// Devolve quantas foram marcadas. Grava na hora, fora do <c>SaveChanges</c>.
    /// </summary>
    Task<int> MarkReceivedAsReadAsync(
        ConversationId conversationId,
        ConversationParty reader,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>As URLs de todas as fotos da conversa, para apagar do bucket.</summary>
    Task<IReadOnlyList<string>> ListImageUrlsAsync(ConversationId conversationId, CancellationToken cancellationToken);

    void Add(Message message);
}
