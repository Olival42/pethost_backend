namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>Situação do reembolso de uma parcela paga.</summary>
public enum RefundStatus
{
    /// <summary>Nada a devolver (ou ainda não pedido).</summary>
    None,

    /// <summary>Pedido ao Stripe; o dinheiro está voltando (o Pix pode levar um tempo).</summary>
    Pending,

    /// <summary>Devolvido ao tutor.</summary>
    Succeeded,

    /// <summary>O Stripe não conseguiu devolver. O admin resolve.</summary>
    Failed,
}
