namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>Em que pé está a cobrança de uma parcela no Stripe (o <c>PaymentIntent</c>).</summary>
public enum ChargeState
{
    /// <summary>Esperando o tutor (escolher o meio, pagar o Pix, autenticar o cartão) ou a tentativa anterior falhou.</summary>
    AwaitingPayment,

    /// <summary>Pago, e o Stripe está confirmando.</summary>
    Processing,

    /// <summary>Pago e confirmado.</summary>
    Succeeded,

    /// <summary>Cancelada: não pode mais ser paga.</summary>
    Canceled,
}
