using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PetHost.Shared.Kernel.Primitives;
using PetHost.Shared.Kernel.Results;
using Stripe;

namespace PetHost.Shared.Infrastructure.Stripe;

/// <summary>
/// Confere a assinatura do webhook (<c>Stripe-Signature</c>, HMAC com o segredo do destino e
/// tolerância de 5 min contra reenvio) e entrega o evento aos <see cref="IStripeWebhookHandler"/>
/// que aceitam o tipo. Tipo que ninguém trata é aceito e ignorado (o Stripe não reenvia).
/// </summary>
public sealed partial class StripeWebhookProcessor(
    IOptions<StripeOptions> options,
    IEnumerable<IStripeWebhookHandler> handlers,
    ILogger<StripeWebhookProcessor> logger)
{
    /// <summary>Janela da assinatura: evento assinado há mais tempo é recusado (reenvio de um evento capturado).</summary>
    public const long ToleranceSeconds = 300;

    public async Task<Result<Unit>> ProcessAsync(string payload, string? signature, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var secrets = options.Value.WebhookSecrets;
        if (secrets.Count == 0)
        {
            LogNotConfigured(logger);
            return Result<Unit>.Failure(StripeWebhookErrors.NotConfigured);
        }

        var stripeEvent = Verify(payload, signature, secrets);
        if (stripeEvent is null)
        {
            LogSignatureRejected(logger);
            return Result<Unit>.Failure(StripeWebhookErrors.SignatureInvalid);
        }

        var received = new StripeWebhookEvent(
            stripeEvent.Id,
            stripeEvent.Type,
            stripeEvent.Account,
            (stripeEvent.Data?.Object as IHasId)?.Id,
            stripeEvent.Livemode,
            new DateTimeOffset(DateTime.SpecifyKind(stripeEvent.Created, DateTimeKind.Utc)));

        var matching = handlers.Where(handler => handler.CanHandle(received.Type)).ToList();
        if (matching.Count == 0)
        {
            LogIgnored(logger, received.Type, received.Id);
            return Result<Unit>.Success(Unit.Value);
        }

        foreach (var handler in matching)
            await handler.HandleAsync(received, cancellationToken).ConfigureAwait(false);

        LogHandled(logger, received.Type, received.Id, matching.Count);
        return Result<Unit>.Success(Unit.Value);
    }

    /// <summary>O evento, se a assinatura confere com algum dos segredos; senão <c>null</c>.</summary>
    private static Event? Verify(string payload, string? signature, IReadOnlyList<string> secrets)
    {
        if (string.IsNullOrWhiteSpace(signature))
            return null;

        foreach (var secret in secrets)
        {
            try
            {
                // Versão da API do evento diferente da do SDK não é motivo para recusar: os
                // handlers só leem tipo e ids e releem o objeto na API.
                return EventUtility.ConstructEvent(payload, signature, secret, ToleranceSeconds, throwOnApiVersionMismatch: false);
            }
            catch (StripeException)
            {
                // Assinatura não confere com este segredo (ou corpo ilegível): tenta o próximo.
            }
        }

        return null;
    }

    [LoggerMessage(EventId = 2100, Level = LogLevel.Warning, Message = "Stripe webhook received but no webhook secret is configured.")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Warning, Message = "Stripe webhook rejected: missing or invalid signature.")]
    private static partial void LogSignatureRejected(ILogger logger);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Information, Message = "Stripe event {EventType} ({EventId}) ignored: no handler.")]
    private static partial void LogIgnored(ILogger logger, string eventType, string eventId);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Information, Message = "Stripe event {EventType} ({EventId}) handled by {HandlerCount} handler(s).")]
    private static partial void LogHandled(ILogger logger, string eventType, string eventId, int handlerCount);
}
