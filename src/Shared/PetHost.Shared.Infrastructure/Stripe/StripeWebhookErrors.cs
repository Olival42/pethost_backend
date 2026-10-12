using PetHost.Shared.Kernel.Errors;

namespace PetHost.Shared.Infrastructure.Stripe;

/// <summary>Erros do endpoint de webhook do Stripe.</summary>
public static class StripeWebhookErrors
{
    /// <summary>Header <c>Stripe-Signature</c> ausente, assinatura que não confere ou vencida (400).</summary>
    public static readonly Error SignatureInvalid =
        Error.Validation("stripeSignature", "The Stripe-Signature header is missing, invalid or expired.");

    /// <summary>Sem segredo de webhook configurado: não dá para conferir (503, o Stripe tenta de novo).</summary>
    public static readonly Error NotConfigured =
        new("STRIPE_WEBHOOK_UNAVAILABLE", "Stripe webhooks are not configured on this server.");
}
