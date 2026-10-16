using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Conversations.Domain.Preferences;

namespace PetHost.Modules.Conversations.Infrastructure.Persistence.Repositories;

/// <summary>Implementação de <see cref="INotificationPreferenceRepository"/> sobre o EF Core.</summary>
internal sealed class NotificationPreferenceRepository(ConversationsDbContext dbContext) : INotificationPreferenceRepository
{
    public Task<NotificationPreference?> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.NotificationPreferences.FirstOrDefaultAsync(p => p.Id == userId, cancellationToken);

    public async Task<IReadOnlySet<Guid>> ListUnreadEmailOptOutsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        if (userIds.Count == 0)
            return new HashSet<Guid>();

        var optedOut = await dbContext.NotificationPreferences
            .AsNoTracking()
            .Where(p => userIds.Contains(p.Id) && !p.UnreadMessagesEmail)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return optedOut.ToHashSet();
    }

    public void Add(NotificationPreference preference) => dbContext.NotificationPreferences.Add(preference);
}
