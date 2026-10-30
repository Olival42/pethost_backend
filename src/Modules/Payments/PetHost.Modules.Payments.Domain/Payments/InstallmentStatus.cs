namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>Situação de uma parcela (uma cobrança no Stripe).</summary>
public enum InstallmentStatus
{
    /// <summary>A vencer (ou vencida, ainda na tolerância). Pode ser paga.</summary>
    Pending,

    /// <summary>O tutor pagou e o Stripe ainda está confirmando (Pix compensando, cartão em análise).</summary>
    Processing,

    /// <summary>O Stripe confirmou o pagamento.</summary>
    Paid,

    /// <summary>Venceu depois do check-in e passou da tolerância. Ainda pode ser paga; o tutor não pede estadia nova até pagar.</summary>
    Overdue,

    /// <summary>Não será cobrada: a reserva foi cancelada ou expirou antes.</summary>
    Cancelled,
}
