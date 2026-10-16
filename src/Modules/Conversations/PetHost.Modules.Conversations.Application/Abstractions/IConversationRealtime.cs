using PetHost.Modules.Conversations.Application.Messages.Responses;

namespace PetHost.Modules.Conversations.Application.Abstractions;

/// <summary>
/// Avisos em tempo real (hub SignalR). Chamados depois que a mudança foi gravada. São
/// best-effort: falha no envio vai para o log e nunca derruba o request — o cliente recupera
/// pelo REST quando reconecta.
/// </summary>
public interface IConversationRealtime
{
    /// <summary>Uma mensagem nova, para as contas <paramref name="userIds"/> (os dois lados da conversa).</summary>
    Task MessageCreatedAsync(IReadOnlyCollection<Guid> userIds, MessageResponse message, CancellationToken cancellationToken);

    /// <summary>O total de mensagens não lidas da conta <paramref name="userId"/> mudou.</summary>
    Task UnreadCountChangedAsync(Guid userId, int total, CancellationToken cancellationToken);
}
