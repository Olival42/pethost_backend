namespace PetHost.Modules.Hosts.Application.Abstractions;

/// <summary>Prazos da manutenção do cadastro de recebimento (vêm da configuração).</summary>
public interface IHostPayoutsPolicy
{
    /// <summary>
    /// Quanto tempo depois de abrir o cadastro (ou da aprovação, se nem abriu) vai cada lembrete.
    /// Vazio = sem lembretes.
    /// </summary>
    IReadOnlyList<TimeSpan> ReminderSchedule { get; }

    /// <summary>De quanto em quanto tempo quem já recebe é relido no Stripe (bloqueio cujo webhook se perdeu).</summary>
    TimeSpan EnabledResyncInterval { get; }
}
