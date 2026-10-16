using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;

namespace PetHost.Modules.Conversations.Infrastructure.Persistence.Repositories;

/// <summary>Implementação de <see cref="IMessageRepository"/> sobre o EF Core.</summary>
internal sealed class MessageRepository(ConversationsDbContext dbContext) : IMessageRepository
{
    /// <summary>
    /// Um <c>UPDATE</c> só, com <c>read_at IS NULL</c> na condição: duas abas marcando ao mesmo
    /// tempo não sobrescrevem a hora da primeira leitura.
    /// </summary>
    public Task<int> MarkReceivedAsReadAsync(
        ConversationId conversationId,
        ConversationParty reader,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        dbContext.Messages
            .Where(m => m.ConversationId == conversationId && m.SentBy != reader && m.ReadAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.ReadAt, now), cancellationToken);

    public Task<Message?> FindInConversationAsync(ConversationId conversationId, MessageId id, CancellationToken cancellationToken) =>
        dbContext.Messages
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id && m.ConversationId == conversationId, cancellationToken);

    public async Task<IReadOnlyList<string>> ListImageUrlsAsync(ConversationId conversationId, CancellationToken cancellationToken) =>
        await dbContext.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.ImageUrl != null)
            .Select(m => m.ImageUrl!)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(Message message) => dbContext.Messages.Add(message);
}
