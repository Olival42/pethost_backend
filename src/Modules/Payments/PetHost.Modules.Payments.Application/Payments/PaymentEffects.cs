using PetHost.Modules.Payments.Domain.Payments;

namespace PetHost.Modules.Payments.Application.Payments;

/// <summary>
/// O que aconteceu com o pagamento numa operação e precisa ir para a trilha e para os avisos.
/// Guardado durante a operação e publicado só depois de gravar (<see cref="PaymentEffectsPublisher"/>):
/// nada é anunciado se a gravação falhar.
/// </summary>
public sealed class PaymentEffects
{
    private readonly List<PaymentEffect> _items = [];

    public IReadOnlyList<PaymentEffect> Items => _items.AsReadOnly();

    public void Add(PaymentEffectKind kind, int installmentNumber, long amountCents, string? detail = null) =>
        _items.Add(new PaymentEffect(kind, installmentNumber, amountCents, detail));

    /// <summary>O que a releitura do Stripe mudou na parcela: pagamento, contestação, reembolso que falhou.</summary>
    public void Add(InstallmentChange change, Installment installment)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(installment);

        if (change.BecamePaid)
            Add(PaymentEffectKind.InstallmentPaid, change.Number, installment.ChargedCents, installment.Method is { } method ? PaymentMethodValues.ToWire(method) : null);

        if (change.DisputeOpened)
            Add(PaymentEffectKind.DisputeOpened, change.Number, installment.AmountCents);

        if (change.DisputeClosed)
            Add(PaymentEffectKind.DisputeClosed, change.Number, installment.AmountCents, DisputeStatusValues.ToWire(change.Dispute));

        if (change.RefundFailed)
            Add(PaymentEffectKind.RefundFailed, change.Number, installment.RefundedCents);
    }
}

/// <summary>Um fato do pagamento a anunciar.</summary>
/// <param name="Detail">Contexto curto: o meio de pagamento, o resultado da contestação.</param>
public sealed record PaymentEffect(PaymentEffectKind Kind, int InstallmentNumber, long AmountCents, string? Detail);

/// <summary>Que fato.</summary>
public enum PaymentEffectKind
{
    /// <summary>O tutor escolheu (ou trocou) o número de parcelas.</summary>
    PlanChosen,

    /// <summary>Uma parcela foi paga.</summary>
    InstallmentPaid,

    /// <summary>Uma parcela vence em breve (lembrete).</summary>
    InstallmentReminder,

    /// <summary>Uma parcela entrou em atraso (depois do check-in). Valor: quanto pagar, com multa e juros.</summary>
    InstallmentOverdue,

    /// <summary>Um reembolso foi pedido ao Stripe.</summary>
    RefundCreated,

    /// <summary>Um reembolso falhou no Stripe.</summary>
    RefundFailed,

    /// <summary>Um repasse foi feito ao anfitrião.</summary>
    PayoutTransferred,

    /// <summary>Um repasse foi estornado do anfitrião.</summary>
    PayoutReversed,

    /// <summary>O estorno do repasse não passou (saldo do anfitrião, conta fechada).</summary>
    PayoutReversalFailed,

    /// <summary>O tutor contestou a cobrança.</summary>
    DisputeOpened,

    /// <summary>A contestação foi decidida.</summary>
    DisputeClosed,

    /// <summary>Uma parcela continua em atraso (lembrete semanal). Valor: quanto pagar, com multa e juros.</summary>
    InstallmentOverdueReminder,
}
