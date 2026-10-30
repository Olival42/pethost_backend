namespace PetHost.Modules.Hosts.Application.Abstractions;

/// <summary>Avisos do cadastro de recebimento (e-mail, em segundo plano).</summary>
public interface IHostPayoutsNotifier
{
    /// <summary>Ao anfitrião: o Stripe liberou os repasses; os cantinhos já podem aparecer.</summary>
    Task NotifyPayoutsEnabledAsync(HostPayoutsNotice notice, CancellationToken cancellationToken);

    /// <summary>Ao anfitrião: lembrete de terminar (ou começar) o cadastro de recebimento.</summary>
    Task NotifyPayoutSetupReminderAsync(HostPayoutsReminder reminder, CancellationToken cancellationToken);

    /// <summary>Ao anfitrião: o Stripe recusou a conta; falar com o suporte.</summary>
    Task NotifyPayoutsRejectedAsync(HostPayoutsNotice notice, CancellationToken cancellationToken);

    /// <summary>Ao admin (endereço da configuração): o Stripe recusou a conta de um anfitrião.</summary>
    Task NotifyAdminPayoutsRejectedAsync(HostPayoutsRejection rejection, CancellationToken cancellationToken);

    /// <summary>
    /// Ao admin: o Stripe bloqueou (ou recusou) os repasses de um anfitrião que tem reservas em
    /// aberto — os repasses ficam retidos e o admin decide se reembolsa os tutores.
    /// </summary>
    Task NotifyAdminPayoutsBlockedAsync(HostPayoutsBlocked blocked, CancellationToken cancellationToken);
}

/// <summary>Alerta ao admin: repasses bloqueados com reservas em aberto. Sem CPF.</summary>
/// <param name="Reason">O <c>disabled_reason</c> do Stripe.</param>
/// <param name="OpenBookingIds">As reservas em aberto do anfitrião (solicitada, aceita, confirmada, em andamento).</param>
public sealed record HostPayoutsBlocked(Guid HostId, string HostName, string? Reason, IReadOnlyList<Guid> OpenBookingIds);

/// <summary>Para quem vai o aviso.</summary>
public sealed record HostPayoutsNotice(string Email, string FullName);

/// <summary>Lembrete do cadastro de recebimento.</summary>
/// <param name="Status">Situação no momento (<c>not_started</c>, <c>in_progress</c> ou <c>restricted</c>): muda o texto.</param>
/// <param name="ReminderNumber">1 no primeiro lembrete, 2 no segundo...</param>
public sealed record HostPayoutsReminder(string Email, string FullName, string Status, int ReminderNumber);

/// <summary>Alerta ao admin. Sem CPF: o admin abre o anfitrião pelo id.</summary>
/// <param name="Reason">O <c>disabled_reason</c> do Stripe (<c>rejected.fraud</c>...).</param>
public sealed record HostPayoutsRejection(Guid HostId, string HostName, string? Reason);
