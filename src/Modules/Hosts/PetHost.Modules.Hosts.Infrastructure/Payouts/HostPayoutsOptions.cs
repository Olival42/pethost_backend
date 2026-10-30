using Microsoft.Extensions.Options;
using PetHost.Modules.Hosts.Application.Abstractions;

namespace PetHost.Modules.Hosts.Infrastructure.Payouts;

/// <summary>
/// Manutenção do cadastro de recebimento (Stripe). Vem do ambiente como
/// <c>HostPayouts__MaintenanceIntervalMinutes</c>, <c>HostPayouts__ReminderDays__0</c> etc.
/// </summary>
public sealed class HostPayoutsOptions
{
    public const string SectionName = "HostPayouts";

    /// <summary>Liga a tarefa em segundo plano. Os testes desligam e chamam a rodada direto.</summary>
    public bool MaintenanceEnabled { get; init; } = true;

    /// <summary>De quantos em quantos minutos a rodada roda (releitura no Stripe + lembretes).</summary>
    public int MaintenanceIntervalMinutes { get; init; } = 60;

    /// <summary>
    /// Dias depois de abrir o cadastro (ou da aprovação, se nem abriu) em que vai cada lembrete,
    /// separados por vírgula. Padrão: <c>3,7</c>. Vazio = sem lembretes. (Texto e não lista: o
    /// binder de configuração soma itens a uma lista com valor padrão em vez de trocar.)
    /// </summary>
    public string ReminderDays { get; init; } = "3,7";

    /// <summary><see cref="ReminderDays"/> lido; <c>null</c> se algum valor não for número inteiro positivo.</summary>
    internal IReadOnlyList<int>? ParsedReminderDays
    {
        get
        {
            var days = new List<int>();
            foreach (var part in ReminderDays.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!int.TryParse(part, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) || value <= 0)
                    return null;

                days.Add(value);
            }

            return [.. days.Order()];
        }
    }

    /// <summary>De quantas em quantas horas quem já recebe é relido no Stripe (bloqueio cujo webhook se perdeu).</summary>
    public int EnabledResyncHours { get; init; } = 24;

    /// <summary>
    /// E-mail que recebe o alerta quando o Stripe recusa a conta de um anfitrião. Vazio = só a
    /// trilha (<c>host.stripe_account_rejected</c>) e o filtro <c>GET /hosts?stripeStatus=rejected</c>.
    /// </summary>
    public string? AdminAlertEmail { get; init; }
}

/// <summary>Configuração inválida derruba a subida.</summary>
internal sealed class HostPayoutsOptionsValidator : IValidateOptions<HostPayoutsOptions>
{
    public ValidateOptionsResult Validate(string? name, HostPayoutsOptions options)
    {
        var failures = new List<string>();

        if (options.MaintenanceIntervalMinutes <= 0)
            failures.Add($"{HostPayoutsOptions.SectionName}:{nameof(HostPayoutsOptions.MaintenanceIntervalMinutes)} must be greater than zero.");

        if (options.EnabledResyncHours <= 0)
            failures.Add($"{HostPayoutsOptions.SectionName}:{nameof(HostPayoutsOptions.EnabledResyncHours)} must be greater than zero.");

        if (options.ParsedReminderDays is null)
            failures.Add($"{HostPayoutsOptions.SectionName}:{nameof(HostPayoutsOptions.ReminderDays)} must be a comma-separated list of days greater than zero (e.g. 3,7).");

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}

/// <summary>Adaptador da porta <see cref="IHostPayoutsPolicy"/> sobre as opções.</summary>
internal sealed class HostPayoutsPolicy(IOptions<HostPayoutsOptions> options) : IHostPayoutsPolicy
{
    public IReadOnlyList<TimeSpan> ReminderSchedule =>
        [.. (options.Value.ParsedReminderDays ?? []).Select(days => TimeSpan.FromDays(days))];

    public TimeSpan EnabledResyncInterval => TimeSpan.FromHours(options.Value.EnabledResyncHours);
}
