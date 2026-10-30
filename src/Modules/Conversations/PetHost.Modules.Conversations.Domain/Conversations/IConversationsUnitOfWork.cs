using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Domain.Conversations;

/// <summary>Confirma a transação do módulo Conversations.</summary>
public interface IConversationsUnitOfWork
{
    /// <summary>
    /// Grava tudo numa transação. A corrida que o banco barra volta como erro, não como exceção
    /// (§15): a mesma dupla abrindo duas conversas ao mesmo tempo dá
    /// <c>409 CONVERSATION_ALREADY_EXISTS</c>; mensagem para uma conversa que acabou de ser apagada
    /// pela retenção dá <c>409 CONVERSATION_CONFLICT</c>.
    /// </summary>
    Task<Result> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Roda <paramref name="work"/> numa transação com a trava da conversa: dois envios na mesma
    /// conversa gravam um depois do outro, e o segundo lê o que o primeiro gravou. Sem a trava, a
    /// última atividade da conversa podia voltar no tempo quando os dois lados escreviam juntos.
    /// </summary>
    Task<T> RunLockedOnConversationAsync<T>(
        ConversationId conversationId,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken);
}
