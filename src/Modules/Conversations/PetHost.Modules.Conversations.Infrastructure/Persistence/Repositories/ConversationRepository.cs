using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Conversations.Domain.Conversations;

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
        await dbContext.Conversations
            .AsNoTracking()
            .Where(c => (c.LastMessageAt ?? c.CreatedAt) < cutoff)
            .OrderBy(c => c.LastMessageAt ?? c.CreatedAt)
            .ThenBy(c => c.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Um <c>DELETE ... WHERE</c> com a condição de inatividade: o Postgres confere de novo na hora
    /// de apagar. Se o envio de uma mensagem gravou antes, a conversa não está mais parada e fica.
    /// As mensagens saem pelo <c>ON DELETE CASCADE</c>.
    /// </summary>
    public async Task<bool> DeleteIfInactiveAsync(ConversationId id, DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var deleted = await dbContext.Conversations
            .Where(c => c.Id == id && (c.LastMessageAt ?? c.CreatedAt) < cutoff)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return deleted > 0;
    }

    public void Add(Conversation conversation) => dbContext.Conversations.Add(conversation);
}
