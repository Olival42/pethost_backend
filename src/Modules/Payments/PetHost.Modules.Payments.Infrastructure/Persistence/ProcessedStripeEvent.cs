namespace PetHost.Modules.Payments.Infrastructure.Persistence;

/// <summary>
/// Um evento do Stripe já tratado pelo módulo (<c>payment.stripe_events</c>). O Stripe pode mandar o
/// mesmo evento mais de uma vez; com o id gravado, a repetição é ignorada. Gravado <b>depois</b> de
/// tratar: evento que falhou não fica marcado e é tratado de novo quando o Stripe reenviar.
/// </summary>
/// <remarks>Sem o corpo do evento: ele traz dados do tutor e não é preciso para reprocessar (a cobrança é relida na API).</remarks>
internal sealed class ProcessedStripeEvent
{
    /// <summary>Construtor só para o EF Core materializar a entidade.</summary>
    private ProcessedStripeEvent()
    {
        StripeEventId = string.Empty;
        Type = string.Empty;
    }

    public ProcessedStripeEvent(string stripeEventId, string type, DateTimeOffset now)
    {
        Id = Guid.CreateVersion7();
        StripeEventId = stripeEventId;
        Type = type;
        ProcessedAt = now;
        CreatedAt = now;
    }

    public const int StripeEventIdMaxLength = 255;
    public const int TypeMaxLength = 80;

    public Guid Id { get; private set; }

    /// <summary>Id do evento no Stripe (<c>evt_...</c>).</summary>
    public string StripeEventId { get; private set; }

    /// <summary>Tipo, ex.: <c>payment_intent.succeeded</c>.</summary>
    public string Type { get; private set; }

    public DateTimeOffset ProcessedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
