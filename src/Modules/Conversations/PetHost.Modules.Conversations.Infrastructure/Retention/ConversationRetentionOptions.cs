namespace PetHost.Modules.Conversations.Infrastructure.Retention;

/// <summary>
/// Tarefa que apaga as conversas paradas (LGPD). Vem do ambiente como
/// <c>ConversationRetention__InactivityDays</c> etc.
/// </summary>
public sealed class ConversationRetentionOptions
{
    public const string SectionName = "ConversationRetention";

    /// <summary>Liga a tarefa em segundo plano. Os testes desligam e chamam o comando direto.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>De quantos em quantos minutos a tarefa roda (padrão: uma vez por dia).</summary>
    public int IntervalMinutes { get; init; } = 1440;

    /// <summary>Dias sem mensagem para a conversa ser apagada (decisão do produto: 90).</summary>
    public int InactivityDays { get; init; } = 90;

    /// <summary>Quantas conversas cada consulta da rodada traz.</summary>
    public int BatchSize { get; init; } = 100;
}
