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

    /// <summary>A mensagem <paramref name="id"/> da conversa <paramref name="conversationId"/>, ou <c>null</c>. Sem rastreamento.</summary>
    Task<Message?> FindInConversationAsync(ConversationId conversationId, MessageId id, CancellationToken cancellationToken);

    /// <summary>As URLs das fotos antigas (bucket público) da conversa, para apagar do bucket.</summary>
    Task<IReadOnlyList<string>> ListImageUrlsAsync(ConversationId conversationId, CancellationToken cancellationToken);

    /// <summary>As chaves das fotos da conversa no bucket privado, para apagar.</summary>
    Task<IReadOnlyList<string>> ListImageKeysAsync(ConversationId conversationId, CancellationToken cancellationToken);

    void Add(Message message);
}
