namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>O que o pagamento precisa pedir ao Stripe (<see cref="PaymentAction"/>).</summary>
public enum PaymentActionKind
{
    /// <summary>Cancelar a cobrança em aberto de uma parcela que não será mais paga.</summary>
    CancelIntent,

    /// <summary>Devolver ao tutor parte ou todo o valor pago de uma parcela.</summary>
    Refund,

    /// <summary>Repassar ao anfitrião a parte dele (90% do que ficou retido) de uma parcela.</summary>
    Transfer,

    /// <summary>Estornar do anfitrião o repasse de uma parcela cuja contestação foi perdida.</summary>
    ReverseTransfer,
}
