namespace PetHost.Modules.Payments.Application.Abstractions;

/// <summary>Avisos do pagamento (e-mail, em segundo plano, pela caixa de saída).</summary>
public interface IPaymentNotifier
{
    /// <summary>Ao tutor ou ao anfitrião: parcela paga, lembrete, atraso, reembolso, repasse.</summary>
    Task NotifyAsync(PaymentNotice notice, CancellationToken cancellationToken);

    /// <summary>
    /// Ao admin (<c>Payments:AdminAlertEmail</c>): o que precisa de gente — contestação, reembolso que
    /// falhou, estorno de repasse, parcela em atraso. Sem e-mail configurado, só a trilha e a lista de pendências.
    /// </summary>
    Task AlertAdminAsync(PaymentAlert alert, CancellationToken cancellationToken);
}

/// <summary>Um aviso a um dos lados.</summary>
/// <param name="AmountCents">
/// O valor de que o aviso trata (a parcela, o reembolso, o repasse). Atraso: quanto pagar agora,
/// com multa e juros. Pagamento: quanto foi pago, com o acréscimo, se houve.
/// </param>
/// <param name="DueAt">Vencimento da parcela (lembrete e atraso).</param>
public sealed record PaymentNotice(
    PaymentNoticeKind Kind,
    string Email,
    string Name,
    Guid BookingId,
    int InstallmentNumber,
    int InstallmentCount,
    long AmountCents,
    DateTimeOffset? DueAt = null);

/// <summary>Qual aviso.</summary>
public enum PaymentNoticeKind
{
    /// <summary>Ao tutor: recebemos o pagamento da parcela.</summary>
    InstallmentPaid,

    /// <summary>Ao tutor: a parcela vence em breve.</summary>
    InstallmentReminder,

    /// <summary>Ao tutor: a parcela está em atraso (estadia já feita); pague para pedir outras.</summary>
    InstallmentOverdue,

    /// <summary>Ao tutor: a parcela continua em atraso (a cada 7 dias), com o valor atualizado.</summary>
    InstallmentOverdueReminder,

    /// <summary>Ao tutor: devolvemos este valor.</summary>
    RefundCreated,

    /// <summary>Ao anfitrião: repassamos este valor para a sua conta.</summary>
    PayoutTransferred,

    /// <summary>Ao anfitrião: o tutor ganhou a contestação e este valor foi estornado do seu repasse.</summary>
    PayoutReversed,
}

/// <summary>Um alerta ao admin.</summary>
/// <param name="Issue">O problema, no vocabulário da lista de pendências (<c>dispute_opened</c>...).</param>
public sealed record PaymentAlert(string Issue, Guid PaymentId, Guid BookingId, int InstallmentNumber, long AmountCents);
