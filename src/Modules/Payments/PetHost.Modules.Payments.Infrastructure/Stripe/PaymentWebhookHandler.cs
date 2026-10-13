using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using PetHost.Modules.Payments.Application.Payments.SyncPaymentIntent;
using PetHost.Modules.Payments.Infrastructure.Persistence;
using PetHost.Shared.Infrastructure.Stripe;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Payments.Infrastructure.Stripe;

/// <summary>
/// Webhooks das cobranças (conta da plataforma): pagamento confirmado, em processamento, recusado
/// ou cancelado; reembolso criado, atualizado ou que falhou; contestação aberta, atualizada ou
/// decidida. O evento é só um aviso: a cobrança é relida na API e o pagamento, reconciliado
/// (<see cref="SyncPaymentIntentCommand"/>).
/// </summary>
/// <remarks>
/// Evento já tratado (<c>payment.stripe_events</c>): ignorado. Cobrança que não é de uma parcela:
/// ignorada (e marcada). Stripe fora do ar ou pagamento alterado ao mesmo tempo: exceção → 500, e o
/// Stripe reenvia o evento mais tarde (nada é marcado). Outras falhas vão para o log.
/// </remarks>
internal sealed partial class PaymentWebhookHandler(
    ICommandHandler<SyncPaymentIntentCommand, SyncPaymentIntentResponse> syncPaymentIntent,
    PaymentsDbContext dbContext,
    TimeProvider timeProvider,
    ILogger<PaymentWebhookHandler> logger) : IStripeWebhookHandler
{
    /// <summary>Os tipos de evento que o módulo trata.</summary>
    public static readonly IReadOnlySet<string> EventTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "payment_intent.succeeded",
        "payment_intent.processing",
        "payment_intent.payment_failed",
        "payment_intent.canceled",
        "charge.refunded",
        "charge.refund.updated",
        "refund.created",
        "refund.updated",
        "refund.failed",
        "charge.dispute.created",
        "charge.dispute.updated",
        "charge.dispute.closed",
        "charge.dispute.funds_withdrawn",
        "charge.dispute.funds_reinstated",
    };

    public bool CanHandle(string eventType) => EventTypes.Contains(eventType);

    public async Task HandleAsync(StripeWebhookEvent stripeEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stripeEvent);

        if (await dbContext.StripeEvents.AsNoTracking().AnyAsync(e => e.StripeEventId == stripeEvent.Id, cancellationToken).ConfigureAwait(false))
        {
            LogDuplicate(logger, stripeEvent.Id, stripeEvent.Type);
            return;
        }

        if (string.IsNullOrWhiteSpace(stripeEvent.ObjectId))
        {
            LogMissingObject(logger, stripeEvent.Id, stripeEvent.Type);
            return;
        }

        var result = await syncPaymentIntent.HandleAsync(new SyncPaymentIntentCommand(stripeEvent.ObjectId), cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            // Stripe fora do ar ou conflito: 500 e o Stripe tenta de novo. Evento inválido não volta a funcionar com retry.
            if (result.FirstError?.Code is { } code && (code.EndsWith("_UNAVAILABLE", StringComparison.Ordinal) || code.EndsWith("_CONFLICT", StringComparison.Ordinal)))
                throw new InvalidOperationException($"Stripe event '{stripeEvent.Id}' ({stripeEvent.Type}) could not be processed now: {code}.");

            LogFailed(logger, stripeEvent.Id, stripeEvent.Type, result.FirstError?.Code ?? "unknown");
            return;
        }

        if (!result.Value!.Matched)
            LogUnknownIntent(logger, stripeEvent.Id, stripeEvent.Type, stripeEvent.ObjectId);

        await MarkProcessedAsync(stripeEvent, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Marca o evento; dois iguais ao mesmo tempo: o segundo bate no índice único e é ignorado.</summary>
    private async Task MarkProcessedAsync(StripeWebhookEvent stripeEvent, CancellationToken cancellationToken)
    {
        dbContext.StripeEvents.Add(new ProcessedStripeEvent(stripeEvent.Id, stripeEvent.Type, timeProvider.GetUtcNow()));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            dbContext.ChangeTracker.Clear();
            LogDuplicate(logger, stripeEvent.Id, stripeEvent.Type);
        }
    }

    [LoggerMessage(EventId = 9200, Level = LogLevel.Information, Message = "Stripe event {EventId} ({EventType}) was already processed; ignored.")]
    private static partial void LogDuplicate(ILogger logger, string eventId, string eventType);

    [LoggerMessage(EventId = 9201, Level = LogLevel.Warning, Message = "Stripe event {EventId} ({EventType}) has no object id; ignored.")]
    private static partial void LogMissingObject(ILogger logger, string eventId, string eventType);

    [LoggerMessage(EventId = 9202, Level = LogLevel.Information, Message = "Stripe event {EventId} ({EventType}) for {StripeObjectId} does not belong to any installment; ignored.")]
    private static partial void LogUnknownIntent(ILogger logger, string eventId, string eventType, string stripeObjectId);

    [LoggerMessage(EventId = 9203, Level = LogLevel.Warning, Message = "Stripe event {EventId} ({EventType}) could not be processed: {ErrorCode}.")]
    private static partial void LogFailed(ILogger logger, string eventId, string eventType, string errorCode);
}
