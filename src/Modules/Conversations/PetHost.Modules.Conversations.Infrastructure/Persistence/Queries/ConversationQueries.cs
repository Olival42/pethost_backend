using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;

namespace PetHost.Modules.Conversations.Infrastructure.Persistence.Queries;

/// <summary>
/// Leitura do módulo (§11): sem rastreamento e projetada direto nas linhas com <c>Select</c> — a
/// última mensagem e as não lidas vêm na mesma consulta da conversa.
/// </summary>
internal sealed class ConversationQueries(ConversationsDbContext dbContext) : IConversationQueries
{
    public async Task<(IReadOnlyList<ConversationRow> Items, long TotalCount)> ListAsync(
        ConversationParticipant participant,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(participant);

        var conversations = Of(participant);

        var total = await conversations.LongCountAsync(cancellationToken).ConfigureAwait(false);
        if (total == 0)
            return ([], 0);

        var onPage = conversations
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);

        var items = await ProjectAsync(onPage, participant.Side, cancellationToken).ConfigureAwait(false);

        return (items, total);
    }

    public async Task<ConversationRow?> GetAsync(ConversationId id, ConversationParty viewer, CancellationToken cancellationToken)
    {
        var rows = await ProjectAsync(dbContext.Conversations.AsNoTracking().Where(c => c.Id == id), viewer, cancellationToken)
            .ConfigureAwait(false);

        return rows.Count == 0 ? null : rows[0];
    }

    public Task<int> CountUnreadAsync(ConversationParticipant participant, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(participant);

        var conversationIds = Of(participant).Select(c => c.Id);

        return dbContext.Messages
            .AsNoTracking()
            .CountAsync(
                m => conversationIds.Contains(m.ConversationId) && m.SentBy != participant.Side && m.ReadAt == null,
                cancellationToken);
    }

    /// <summary>
    /// Da mais nova para a mais antiga, por (<c>created_at</c>, <c>id</c>). O cursor compara os dois
    /// juntos (<c>(created_at, id) &lt; (...)</c>, em SQL): duas mensagens no mesmo instante não
    /// repetem nem somem entre páginas.
    /// </summary>
    public async Task<IReadOnlyList<MessageRow>?> ListMessagesAsync(
        ConversationId id,
        MessageId? before,
        int take,
        CancellationToken cancellationToken)
    {
        IQueryable<Message> messages;

        if (before is { } cursor)
        {
            var anchors = await dbContext.Messages
                .AsNoTracking()
                .Where(m => m.Id == cursor && m.ConversationId == id)
                .Select(m => m.CreatedAt)
                .Take(1)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (anchors.Count == 0)
                return null;

            var anchorAt = anchors[0];
            messages = dbContext.Messages.FromSql(
                $"SELECT * FROM conversation.messages WHERE conversation_id = {id.Value} AND (created_at, id) < ({anchorAt}, {cursor.Value})");
        }
        else
        {
            messages = dbContext.Messages.Where(m => m.ConversationId == id);
        }

        return await messages
            .AsNoTracking()
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(take)
            .Select(m => new MessageRow(m.Id, m.ConversationId, m.SentBy, m.Body, m.ImageUrl, m.BookingId, m.CreatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private IQueryable<Conversation> Of(ConversationParticipant participant) =>
        participant.Side == ConversationParty.Owner
            ? dbContext.Conversations.AsNoTracking().Where(c => c.OwnerId == participant.ProfileId)
            : dbContext.Conversations.AsNoTracking().Where(c => c.HostId == participant.ProfileId);

    /// <summary>
    /// As conversas (na ordem de <paramref name="conversations"/>) com as não lidas de
    /// <paramref name="viewer"/> — as do outro lado — e a última mensagem de cada.
    /// </summary>
    /// <remarks>
    /// Duas consultas: a da página e a da última mensagem só das conversas dela. Numa consulta só, o
    /// EF numera as mensagens de todas as conversas do banco antes de juntar.
    /// </remarks>
    private async Task<IReadOnlyList<ConversationRow>> ProjectAsync(
        IQueryable<Conversation> conversations,
        ConversationParty viewer,
        CancellationToken cancellationToken)
    {
        var heads = await conversations
            .Select(c => new
            {
                c.Id,
                c.OwnerId,
                c.HostId,
                c.LastMessageAt,
                c.CreatedAt,
                Unread = dbContext.Messages.Count(m => m.ConversationId == c.Id && m.SentBy != viewer && m.ReadAt == null),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (heads.Count == 0)
            return [];

        var ids = heads.Select(h => h.Id).ToList();

        var lastMessages = await dbContext.Messages
            .AsNoTracking()
            .Where(m => ids.Contains(m.ConversationId))
            .GroupBy(m => m.ConversationId)
            .Select(g => g
                .OrderByDescending(m => m.CreatedAt)
                .ThenByDescending(m => m.Id)
                .Select(m => new MessageRow(m.Id, m.ConversationId, m.SentBy, m.Body, m.ImageUrl, m.BookingId, m.CreatedAt))
                .First())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lastByConversation = lastMessages.ToDictionary(m => m.ConversationId);

        return [.. heads.Select(h => new ConversationRow(
            h.Id,
            h.OwnerId,
            h.HostId,
            h.LastMessageAt,
            h.CreatedAt,
            lastByConversation.GetValueOrDefault(h.Id),
            h.Unread))];
    }
}
