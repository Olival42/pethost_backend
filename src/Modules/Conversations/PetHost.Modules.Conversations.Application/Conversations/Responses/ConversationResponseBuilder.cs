using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Messages.Responses;

namespace PetHost.Modules.Conversations.Application.Conversations.Responses;

/// <summary>
/// Junta às linhas lidas do banco o nome e a foto dos dois lados, numa consulta por módulo para a
/// página inteira (nunca uma por conversa).
/// </summary>
public sealed class ConversationResponseBuilder(IConversationParties conversationParties, MessagePhotoLinks photoLinks)
{
    public async Task<ConversationResponse> BuildAsync(ConversationRow row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);

        var responses = await BuildAsync([row], cancellationToken).ConfigureAwait(false);

        return responses[0];
    }

    public async Task<IReadOnlyList<ConversationResponse>> BuildAsync(
        IReadOnlyList<ConversationRow> rows,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
            return [];

        var owners = await conversationParties
            .GetOwnersAsync([.. rows.Select(r => r.OwnerId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        var hosts = await conversationParties
            .GetHostsAsync([.. rows.Select(r => r.HostId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        var lastMessages = new Dictionary<Guid, MessageResponse>();
        foreach (var row in rows.Where(r => r.LastMessage is not null))
            lastMessages[row.Id.Value] = await photoLinks.ToResponseAsync(row.LastMessage!, cancellationToken).ConfigureAwait(false);

        return [.. rows.Select(row => new ConversationResponse(
            row.Id.Value,
            row.OwnerId,
            Summary(owners.GetValueOrDefault(row.OwnerId)),
            row.HostId,
            Summary(hosts.GetValueOrDefault(row.HostId)),
            lastMessages.GetValueOrDefault(row.Id.Value),
            row.UnreadCount,
            row.Blocked,
            row.BlockedByViewer,
            row.BlockedByModeration,
            row.LastMessageAt,
            row.CreatedAt))];
    }

    private static PartySummary? Summary(PartyInfo? party) =>
        party is null ? null : new PartySummary(party.Id, party.Name, party.AvatarUrl);
}
