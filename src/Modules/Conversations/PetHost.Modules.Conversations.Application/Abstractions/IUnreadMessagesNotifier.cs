namespace PetHost.Modules.Conversations.Application.Abstractions;

/// <summary>
/// Manda o resumo de "mensagens não lidas" para quem não abriu o app. A Infrastructure envia por
/// e-mail, pela caixa de saída. Nunca leva o texto das mensagens nem as fotos: só quem mandou e quantas.
/// </summary>
public interface IUnreadMessagesNotifier
{
    Task SendAsync(UnreadDigest digest, CancellationToken cancellationToken);
}

/// <summary>Um resumo para uma conta: as conversas com mensagens esperando por ela.</summary>
/// <param name="Name">Nome da pessoa, para a saudação.</param>
public sealed record UnreadDigest(string Email, string Name, IReadOnlyList<UnreadDigestItem> Items);

/// <summary>Uma conversa do resumo.</summary>
/// <param name="SenderName">Quem mandou (empresa pelo nome fantasia).</param>
/// <param name="UnreadCount">Quantas mensagens dessa pessoa ainda não foram lidas.</param>
public sealed record UnreadDigestItem(string SenderName, int UnreadCount);
