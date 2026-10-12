namespace PetHost.Shared.Infrastructure.Stripe;

/// <summary>
/// Quem reage a um tipo de evento do Stripe. Cada módulo registra os seus
/// (<c>AddScoped&lt;IStripeWebhookHandler, ...&gt;</c>); o endpoint do webhook confere a
/// assinatura uma vez e entrega o evento a todos os que aceitam o tipo.
/// </summary>
/// <remarks>
/// O Stripe pode mandar o mesmo evento mais de uma vez e fora de ordem: o handler precisa ser
/// idempotente. O jeito mais simples é usar o evento só como aviso e reler o objeto na API.
/// Lançar exceção devolve 500, e o Stripe tenta de novo mais tarde.
/// </remarks>
public interface IStripeWebhookHandler
{
    /// <summary>Trata este tipo de evento (ex.: <c>account.updated</c>)?</summary>
    bool CanHandle(string eventType);

    Task HandleAsync(StripeWebhookEvent stripeEvent, CancellationToken cancellationToken);
}

/// <summary>O que interessa de um evento do Stripe, já com a assinatura conferida.</summary>
/// <param name="Id">Id do evento (<c>evt_...</c>).</param>
/// <param name="Type">Tipo (<c>account.updated</c>, <c>payment_intent.succeeded</c>...).</param>
/// <param name="ConnectedAccountId">Conta conectada de onde veio (<c>acct_...</c>), em eventos de Connect.</param>
/// <param name="ObjectId">Id do objeto do evento (a conta, o pagamento...).</param>
/// <param name="LiveMode">Evento do modo real. Em teste é sempre <c>false</c>.</param>
public sealed record StripeWebhookEvent(
    string Id,
    string Type,
    string? ConnectedAccountId,
    string? ObjectId,
    bool LiveMode,
    DateTimeOffset CreatedAt);
