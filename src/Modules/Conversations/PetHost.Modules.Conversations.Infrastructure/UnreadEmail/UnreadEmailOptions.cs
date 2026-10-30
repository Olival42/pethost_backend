namespace PetHost.Modules.Conversations.Infrastructure.UnreadEmail;

/// <summary>
/// E-mail de "mensagens não lidas" (seção <c>ConversationUnreadEmail</c>). Vem do ambiente como
/// <c>ConversationUnreadEmail__DelayMinutes</c> etc.
/// </summary>
public sealed class UnreadEmailOptions
{
    public const string SectionName = "ConversationUnreadEmail";

    /// <summary>Liga a tarefa em segundo plano. Os testes desligam e chamam o comando direto.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>De quantos em quantos minutos a tarefa olha as não lidas.</summary>
    public int IntervalMinutes { get; init; } = 5;

    /// <summary>Quanto tempo a mensagem fica sem ler antes do e-mail (decisão do produto: 15).</summary>
    public int DelayMinutes { get; init; } = 15;

    /// <summary>Intervalo mínimo entre dois e-mails da mesma conversa para a mesma pessoa.</summary>
    public int CooldownMinutes { get; init; } = 60;

    /// <summary>Quantas conversas, de cada lado, cada rodada olha.</summary>
    public int BatchSize { get; init; } = 200;

    /// <summary>Endereço do app, para o botão do e-mail. Vazio = o e-mail só pede para abrir o app.</summary>
    public string? AppUrl { get; init; }
}
