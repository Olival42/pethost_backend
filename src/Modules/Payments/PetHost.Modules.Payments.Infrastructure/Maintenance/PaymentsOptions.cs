namespace PetHost.Modules.Payments.Infrastructure.Maintenance;

/// <summary>Configuração do módulo Payments (seção <c>Payments</c>).</summary>
public sealed class PaymentsOptions
{
    public const string SectionName = "Payments";

    /// <summary>Liga a manutenção periódica dos pagamentos (os testes desligam e chamam o comando direto).</summary>
    public bool MaintenanceEnabled { get; init; } = true;

    /// <summary>
    /// De quantos em quantos minutos a manutenção roda: releitura do que pode ter mudado sem
    /// webhook, parcela vencida, lembretes, reembolso ou repasse que falhou.
    /// </summary>
    public int MaintenanceIntervalMinutes { get; init; } = 15;

    /// <summary>
    /// Quem recebe os alertas que precisam de gente (contestação, reembolso que falhou, estorno de
    /// repasse, parcela em atraso). Vazio = só a trilha e a lista de pendências do admin.
    /// </summary>
    public string? AdminAlertEmail { get; init; }
}
