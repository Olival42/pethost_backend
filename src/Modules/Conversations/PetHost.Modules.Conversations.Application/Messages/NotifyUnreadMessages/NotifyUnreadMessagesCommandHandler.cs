using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Preferences;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Conversations.Application.Messages.NotifyUnreadMessages;

/// <summary>
/// E-mail de "mensagens não lidas" para quem não abriu o app: mensagem do outro lado sem ler há
/// <see cref="NotifyUnreadMessagesCommand.DelayMinutes"/> minutos gera um resumo (quem mandou e
/// quantas, nunca o texto). Um e-mail por pessoa, com todas as conversas dela na rodada.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Cada (conversa, lado) guarda quando recebeu o último e-mail: só mensagem nova depois dele, e
/// passado o intervalo mínimo (<see cref="NotifyUnreadMessagesCommand.CooldownMinutes"/>), gera outro.
/// Ler a conversa (marcar como lida) tira a pendência.</item>
/// <item>Perfil ou conta inativo ou suspenso não recebe, nem a conta que desligou o aviso
/// (<see cref="NotificationPreference"/>); a pendência é dada como tratada, para não voltar a cada
/// rodada.</item>
/// <item>A marcação é gravada depois que o e-mail foi para a caixa de saída: uma falha entre os dois
/// repete o e-mail na próxima rodada, nunca o perde.</item>
/// </list>
/// </remarks>
public sealed class NotifyUnreadMessagesCommandHandler(
    IConversationQueries conversationQueries,
    IConversationRepository conversationRepository,
    IConversationParties conversationParties,
    INotificationPreferenceRepository preferenceRepository,
    IUnreadMessagesNotifier notifier,
    TimeProvider timeProvider) : ICommandHandler<NotifyUnreadMessagesCommand, NotifyUnreadMessagesResponse>
{
    public async Task<Result<NotifyUnreadMessagesResponse>> HandleAsync(
        NotifyUnreadMessagesCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var now = timeProvider.GetUtcNow();
        var rows = await conversationQueries
            .ListUnreadToNotifyAsync(now.AddMinutes(-command.DelayMinutes), now.AddMinutes(-command.CooldownMinutes), command.BatchSize, cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
            return Result<NotifyUnreadMessagesResponse>.Success(new NotifyUnreadMessagesResponse(0));

        var sent = 0;

        foreach (var side in rows.GroupBy(r => r.Recipient))
        {
            var recipients = side.GroupBy(r => r.RecipientProfileId).ToList();
            var contacts = await conversationParties
                .GetContactsAsync(side.Key, [.. recipients.Select(r => r.Key)], cancellationToken)
                .ConfigureAwait(false);
            var optedOut = await preferenceRepository
                .ListUnreadEmailOptOutsAsync([.. contacts.Values.Select(c => c.UserId)], cancellationToken)
                .ConfigureAwait(false);
            var senders = await SenderNamesAsync(side.Key, [.. side.Select(r => r.SenderProfileId).Distinct()], cancellationToken)
                .ConfigureAwait(false);

            foreach (var recipient in recipients)
            {
                if (contacts.GetValueOrDefault(recipient.Key) is { CanBeNotified: true } contact && !optedOut.Contains(contact.UserId))
                {
                    var items = recipient
                        .Select(r => new UnreadDigestItem(senders.GetValueOrDefault(r.SenderProfileId, "Alguém"), r.UnreadCount))
                        .ToList();

                    await notifier.SendAsync(new UnreadDigest(contact.Email, contact.Name, items), cancellationToken).ConfigureAwait(false);
                    sent++;
                }

                foreach (var row in recipient)
                {
                    await conversationRepository
                        .MarkUnreadNotifiedAsync(row.ConversationId, row.Recipient, now, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        return Result<NotifyUnreadMessagesResponse>.Success(new NotifyUnreadMessagesResponse(sent));
    }

    /// <summary>O nome de quem mandou: do lado oposto a quem recebe.</summary>
    private async Task<IReadOnlyDictionary<Guid, string>> SenderNamesAsync(
        ConversationParty recipient,
        IReadOnlyCollection<Guid> senderIds,
        CancellationToken cancellationToken)
    {
        var parties = recipient == ConversationParty.Owner
            ? await conversationParties.GetHostsAsync(senderIds, cancellationToken).ConfigureAwait(false)
            : await conversationParties.GetOwnersAsync(senderIds, cancellationToken).ConfigureAwait(false);

        return parties.ToDictionary(p => p.Key, p => p.Value.Name);
    }
}
