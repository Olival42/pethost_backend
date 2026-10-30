using PetHost.Shared.Kernel.Domain;

namespace PetHost.Modules.Conversations.Domain.Preferences;

/// <summary>
/// O que a conta quer receber do chat fora do app. Espelha <c>conversation.notification_preferences</c>.
/// O id é o da conta (<c>auth.users.id</c>, FK lógica): tutor e anfitrião da mesma pessoa são contas
/// diferentes e escolhem cada um. Conta sem linha recebe tudo (o padrão).
/// </summary>
public sealed class NotificationPreference : Entity<Guid>
{
    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private NotificationPreference()
    {
    }

    private NotificationPreference(Guid userId, bool unreadMessagesEmail, DateTimeOffset now)
        : base(userId)
    {
        UnreadMessagesEmail = unreadMessagesEmail;
        UpdatedAt = now;
    }

    /// <summary>Recebe o e-mail de "mensagens não lidas".</summary>
    public bool UnreadMessagesEmail { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>A escolha da conta <paramref name="userId"/>. Id vazio é bug de quem chama.</summary>
    public static NotificationPreference Create(Guid userId, bool unreadMessagesEmail, DateTimeOffset now)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("The user id cannot be empty.", nameof(userId));

        return new NotificationPreference(userId, unreadMessagesEmail, now);
    }

    /// <summary>Troca a escolha. Devolve se mudou.</summary>
    public bool SetUnreadMessagesEmail(bool enabled, DateTimeOffset now)
    {
        if (UnreadMessagesEmail == enabled)
            return false;

        UnreadMessagesEmail = enabled;
        UpdatedAt = now;

        return true;
    }
}
