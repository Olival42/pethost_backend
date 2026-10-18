using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Reports;

namespace PetHost.Modules.Conversations.Infrastructure.Persistence.Repositories;

/// <summary>Implementação de <see cref="IConversationRepository"/> sobre o EF Core.</summary>
internal sealed class ConversationRepository(ConversationsDbContext dbContext) : IConversationRepository
{
    public Task<Conversation?> GetByIdAsync(ConversationId id, CancellationToken cancellationToken) =>
        dbContext.Conversations.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task ReloadAsync(Conversation conversation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        // Apagada pela retenção no meio do envio: a entrada fica Detached e a gravação da mensagem
        // cai na FK, que o Unit of Work traduz em 409 CONVERSATION_CONFLICT.
        return dbContext.Entry(conversation).ReloadAsync(cancellationToken);
    }

    public Task<Conversation?> FindByPartiesAsync(Guid ownerId, Guid hostId, CancellationToken cancellationToken) =>
        dbContext.Conversations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.OwnerId == ownerId && c.HostId == hostId, cancellationToken);

    public async Task<IReadOnlyList<Conversation>> ListInactiveAsync(
        DateTimeOffset cutoff,
        int skip,
        int take,
        CancellationToken cancellationToken) =>
        await Purgeable(cutoff)
            .AsNoTracking()
            .OrderBy(c => c.LastMessageAt ?? c.CreatedAt)
            .ThenBy(c => c.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Um <c>DELETE ... WHERE</c> com a condição de inatividade (e de moderação): o Postgres confere
    /// de novo na hora de apagar. Se o envio de uma mensagem gravou antes, a conversa não está mais
    /// parada e fica; se chegou uma denúncia ou um bloqueio, também. As mensagens e as denúncias já
    /// analisadas saem pelo <c>ON DELETE CASCADE</c>.
    /// </summary>
    public async Task<bool> DeleteIfInactiveAsync(ConversationId id, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var deleted = await Purgeable(cutoff)
            .Where(c => c.Id == id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return deleted > 0;
    }

    public async Task<IReadOnlyList<Conversation>> ListByPartyAsync(ConversationParty party, Guid partyId, CancellationToken cancellationToken) =>
        await (party == ConversationParty.Owner
                ? dbContext.Conversations.Where(c => c.OwnerId == partyId)
                : dbContext.Conversations.Where(c => c.HostId == partyId))
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<bool> DeleteAsync(ConversationId id, CancellationToken cancellationToken)
    {
        var deleted = await dbContext.Conversations
            .Where(c => c.Id == id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return deleted > 0;
    }

    public async Task MarkUnreadNotifiedAsync(ConversationId id, ConversationParty recipient, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var conversation = dbContext.Conversations.Where(c => c.Id == id);

        if (recipient == ConversationParty.Owner)
            await conversation.ExecuteUpdateAsync(set => set.SetProperty(c => c.OwnerUnreadNotifiedAt, at), cancellationToken).ConfigureAwait(false);
        else
            await conversation.ExecuteUpdateAsync(set => set.SetProperty(c => c.HostUnreadNotifiedAt, at), cancellationToken).ConfigureAwait(false);
    }

    public void Add(Conversation conversation) => dbContext.Conversations.Add(conversation);

    /// <summary>
    /// O que a retenção pode apagar: sem atividade desde <paramref name="cutoff"/>, sem bloqueio (o
    /// bloqueio some junto com a conversa, e o tutor poderia puxar conversa de novo) e sem denúncia
    /// esperando o admin (ele precisa ver a mensagem e a foto).
    /// </summary>
    private IQueryable<Conversation> Purgeable(DateTimeOffset cutoff) =>
        dbContext.Conversations.Where(c =>
            (c.LastMessageAt ?? c.CreatedAt) < cutoff
            && c.BlockedBy == null
            && c.ModerationBlockedAt == null
            && !dbContext.MessageReports.Any(r => r.ConversationId == c.Id && r.Status == ReportStatus.Pending));
}
