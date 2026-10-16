namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>Situação do pagamento da reserva como um todo (o plano de parcelas).</summary>
public enum PaymentStatus
{
    /// <summary>Plano escolhido; a 1ª parcela ainda não foi paga.</summary>
    Pending,

    /// <summary>A reserva está paga em parte: há parcelas a vencer.</summary>
    Active,

    /// <summary>Todas as parcelas pagas.</summary>
    Paid,

    /// <summary>A reserva foi cancelada ou expirou: as parcelas em aberto foram canceladas e o que foi pago segue a regra de reembolso.</summary>
    Cancelled,
}
