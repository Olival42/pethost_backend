using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Messages.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;

namespace PetHost.Modules.Conversations.Application.Conversations;

/// <summary>
/// Quem recebe cada aviso em tempo real. Roda depois que a mensagem foi gravada: a mensagem vai
/// para as contas dos dois lados (quem enviou vê nas outras abas e aparelhos), e o contador de não
/// lidas vai só para quem recebeu.
/// </summary>
public sealed class ConversationNotifications(
    IConversationParties conversationParties,
    IConversationQueries conversationQueries,
    IConversationRealtime conversationRealtime)
{
    /// <param name="sentBy">O lado que enviou: o contador muda para o outro.</param>
    public async Task MessageSentAsync(
        Conversation conversation,
        ConversationParty sentBy,
        MessageResponse message,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(message);

        var owners = await conversationParties.GetOwnersAsync([conversation.OwnerId], cancellationToken).ConfigureAwait(false);
        var hosts = await conversationParties.GetHostsAsync([conversation.HostId], cancellationToken).ConfigureAwait(false);

        var owner = owners.GetValueOrDefault(conversation.OwnerId);
        var host = hosts.GetValueOrDefault(conversation.HostId);

        Guid[] accounts = [.. new[] { owner, host }.OfType<PartyInfo>().Select(party => party.UserId)];
        if (accounts.Length > 0)
            await conversationRealtime.MessageCreatedAsync(accounts, message, cancellationToken).ConfigureAwait(false);

        var recipientSide = ConversationPartyValues.Other(sentBy);
        var recipient = recipientSide == ConversationParty.Owner ? owner : host;
        if (recipient is null)
            return;

        var unread = await conversationQueries
            .CountUnreadAsync(new ConversationParticipant(recipientSide, recipient.Id), cancellationToken)
            .ConfigureAwait(false);

        await conversationRealtime.UnreadCountChangedAsync(recipient.UserId, unread, cancellationToken).ConfigureAwait(false);
    }
}
