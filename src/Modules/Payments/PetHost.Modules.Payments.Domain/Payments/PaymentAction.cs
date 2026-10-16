namespace PetHost.Modules.Payments.Domain.Payments;

/// <summary>
/// Um pedido ao Stripe decidido pelo agregado (<see cref="Payment.Reconcile"/>). A Application
/// executa e devolve o resultado ao agregado (<c>Record...</c>); o agregado nunca chama o Stripe.
/// </summary>
/// <param name="StripeObjectId">
/// Sobre o quê: o <c>PaymentIntent</c> (cancelar, reembolsar), a cobrança de origem do repasse
/// (<c>ch_...</c>) ou a transferência a estornar (<c>tr_...</c>).
/// </param>
/// <param name="AmountCents">Quanto (reembolso, repasse ou estorno), em centavos. Zero para cancelar.</param>
/// <param name="PlatformFeeCents">No repasse: o que fica com o PetHost (10% do retido).</param>
public sealed record PaymentAction(
    PaymentActionKind Kind,
    int InstallmentNumber,
    string StripeObjectId,
    long AmountCents = 0,
    long PlatformFeeCents = 0);
