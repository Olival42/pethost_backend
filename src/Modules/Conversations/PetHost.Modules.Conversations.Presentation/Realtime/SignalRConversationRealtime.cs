using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Application.Messages.Responses;

namespace PetHost.Modules.Conversations.Presentation.Realtime;

/// <summary>
/// Implementa a porta <see cref="IConversationRealtime"/> com o <see cref="ConversationsHub"/>.
/// Fica na Presentation porque é ela quem conhece o hub (a Infrastructure não referencia a
/// Presentation, §4).
/// </summary>
/// <remarks>
/// Best-effort: a mensagem já está gravada quando isto roda. Falha no envio vai para o log e nunca
/// derruba o request; o cliente recupera pelo REST quando reconecta. O envio não usa o
/// <c>CancellationToken</c> do request: quem enviou fechar o app não pode impedir o aviso ao outro lado.
/// </remarks>
internal sealed partial class SignalRConversationRealtime(
    IHubContext<ConversationsHub> hubContext,
    ILogger<SignalRConversationRealtime> logger) : IConversationRealtime
{
    public async Task MessageCreatedAsync(IReadOnlyCollection<Guid> userIds, MessageResponse message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        ArgumentNullException.ThrowIfNull(message);

        if (userIds.Count == 0)
            return;

        try
        {
            await hubContext.Clients
                .Users([.. userIds.Select(id => id.ToString())])
                .SendAsync(ConversationEvents.MessageCreated, message, CancellationToken.None)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Aviso em tempo real é best-effort: a mensagem já foi gravada.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogMessageCreatedFailed(logger, message.ConversationId, exception);
        }
    }

    public async Task UnreadCountChangedAsync(Guid userId, int total, CancellationToken cancellationToken)
    {
        try
        {
            await hubContext.Clients
                .User(userId.ToString())
                .SendAsync(ConversationEvents.UnreadCountChanged, new UnreadCountResponse(total), CancellationToken.None)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Aviso em tempo real é best-effort: o contador também sai pelo REST.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogUnreadCountFailed(logger, exception);
        }
    }

    public async Task BlockChangedAsync(IReadOnlyCollection<Guid> userIds, ConversationBlockResponse change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        ArgumentNullException.ThrowIfNull(change);

        if (userIds.Count == 0)
            return;

        try
        {
            await hubContext.Clients
                .Users([.. userIds.Select(id => id.ToString())])
                .SendAsync(ConversationEvents.ConversationBlockChanged, change, CancellationToken.None)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Aviso em tempo real é best-effort: o bloqueio já foi gravado.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogBlockChangedFailed(logger, change.ConversationId, exception);
        }
    }

    [LoggerMessage(EventId = 9100, Level = LogLevel.Warning, Message = "Realtime: message of conversation {ConversationId} was saved but could not be pushed.")]
    private static partial void LogMessageCreatedFailed(ILogger logger, Guid conversationId, Exception exception);

    [LoggerMessage(EventId = 9101, Level = LogLevel.Warning, Message = "Realtime: unread count could not be pushed.")]
    private static partial void LogUnreadCountFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9102, Level = LogLevel.Warning, Message = "Realtime: block change of conversation {ConversationId} was saved but could not be pushed.")]
    private static partial void LogBlockChangedFailed(ILogger logger, Guid conversationId, Exception exception);
}
