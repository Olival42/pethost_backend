namespace PetHost.Modules.Conversations.Presentation.Realtime;

/// <summary>Nomes dos eventos que o <see cref="ConversationsHub"/> envia. Contrato com o front: renomear quebra o app.</summary>
public static class ConversationEvents
{
    /// <summary>
    /// Mensagem nova, para as contas dos dois lados da conversa (quem enviou recebe nas outras
    /// abas e aparelhos). O conteúdo é a mensagem, no mesmo formato de <c>GET .../messages</c>.
    /// </summary>
    public const string MessageCreated = "messageCreated";

    /// <summary>O total de não lidas da conta mudou. O conteúdo é <c>{ "total": n }</c>.</summary>
    public const string UnreadCountChanged = "unreadCountChanged";
}
