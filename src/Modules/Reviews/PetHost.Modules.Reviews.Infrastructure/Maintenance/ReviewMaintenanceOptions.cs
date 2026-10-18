namespace PetHost.Modules.Reviews.Infrastructure.Maintenance;

/// <summary>
/// Tarefa em segundo plano das avaliações (seção <c>ReviewMaintenance</c>): o lembrete de avaliar e
/// o aviso das avaliações publicadas no fim do prazo. Vem do ambiente como
/// <c>ReviewMaintenance__ReminderDelayHours</c> etc.
/// </summary>
public sealed class ReviewMaintenanceOptions
{
    public const string SectionName = "ReviewMaintenance";

    /// <summary>Liga a tarefa. Os testes desligam e chamam os comandos direto.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>De quantos em quantos minutos a tarefa roda.</summary>
    public int IntervalMinutes { get; init; } = 60;

    /// <summary>Quantas horas depois do check-out lembrar quem ainda não avaliou (padrão: 1 dia).</summary>
    public int ReminderDelayHours { get; init; } = 24;

    /// <summary>Quantas estadias (lembrete) e avaliações (aviso) cada rodada olha.</summary>
    public int BatchSize { get; init; } = 200;
}
