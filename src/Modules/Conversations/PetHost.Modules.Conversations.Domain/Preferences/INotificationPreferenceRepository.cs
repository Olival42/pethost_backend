namespace PetHost.Modules.Conversations.Domain.Preferences;

/// <summary>Acesso a <see cref="NotificationPreference"/>. A Application só conhece esta interface (§11).</summary>
public interface INotificationPreferenceRepository
{
    /// <summary>A escolha da conta, rastreada, ou <c>null</c> se ela nunca mudou (recebe tudo).</summary>
    Task<NotificationPreference?> GetAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Das contas <paramref name="userIds"/>, as que desligaram o e-mail de mensagens não lidas.</summary>
    Task<IReadOnlySet<Guid>> ListUnreadEmailOptOutsAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);

    void Add(NotificationPreference preference);
}
