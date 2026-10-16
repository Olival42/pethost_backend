namespace PetHost.Modules.Conversations.Domain.Conversations;

/// <summary>Acesso a <see cref="Conversation"/>. A Application só conhece esta interface (§11).</summary>
public interface IConversationRepository
{
    /// <summary>Pelo id. Rastreado: quem chama pode alterar e gravar.</summary>
    Task<Conversation?> GetByIdAsync(ConversationId id, CancellationToken cancellationToken);

    /// <summary>
    /// Relê do banco a conversa já rastreada, descartando o que estava em memória. Usado dentro da
    /// trava da conversa (<see cref="IConversationsUnitOfWork.RunLockedOnConversationAsync{T}"/>), para
    /// aplicar a mensagem nova sobre o que outro envio acabou de gravar.
    /// </summary>
    Task ReloadAsync(Conversation conversation, CancellationToken cancellationToken);

    /// <summary>A conversa da dupla tutor–anfitrião, ou <c>null</c> se ainda não existe.</summary>
    Task<Conversation?> FindByPartiesAsync(Guid ownerId, Guid hostId, CancellationToken cancellationToken);

    /// <summary>
    /// Conversas sem atividade desde <paramref name="cutoff"/>, da mais antiga para a mais nova,
    /// pulando as <paramref name="skip"/> primeiras. Sem rastreamento.
    /// </summary>
    Task<IReadOnlyList<Conversation>> ListInactiveAsync(
        DateTimeOffset cutoff,
        int skip,
        int take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Apaga a conversa (as mensagens vão junto) se ela continua sem atividade desde
    /// <paramref name="cutoff"/>. A condição é conferida pelo banco no momento do apagamento:
    /// mensagem que chegou depois da leitura salva a conversa. Devolve se apagou.
    /// </summary>
    Task<bool> DeleteIfInactiveAsync(ConversationId id, DateTimeOffset cutoff, CancellationToken cancellationToken);

    void Add(Conversation conversation);
}
