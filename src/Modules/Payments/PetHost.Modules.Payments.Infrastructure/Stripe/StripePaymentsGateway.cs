using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using PetHost.Modules.Payments.Application.Abstractions;
using PetHost.Modules.Payments.Domain.Errors;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Infrastructure.Stripe;
using PetHost.Shared.Kernel.Results;
using Stripe;
using DomainPaymentMethod = PetHost.Modules.Payments.Domain.Payments.PaymentMethod;
using DomainRefundStatus = PetHost.Modules.Payments.Domain.Payments.RefundStatus;

namespace PetHost.Modules.Payments.Infrastructure.Stripe;

/// <summary>
/// Adaptador da porta <see cref="IStripePayments"/> sobre o SDK do Stripe.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cobrança e transferência separadas</b> (escopo, seção 6): a cobrança é da plataforma (sem
/// <c>on_behalf_of</c> nem <c>application_fee</c>) e leva o <c>transfer_group</c> da reserva; o
/// repasse é uma <c>Transfer</c> para a conta conectada do anfitrião com <c>source_transaction</c> =
/// a cobrança da parcela — o dinheiro sai daquela cobrança, mesmo que ainda não esteja disponível
/// no saldo (o Pix e o cartão liquidam em dias diferentes).
/// </para>
/// <para>
/// <b>Checkout transparente</b>: <c>PaymentIntent</c> com <c>card</c> e <c>pix</c>; o app abre o
/// Payment Element com o <c>client_secret</c> e o QR code do Pix aparece na própria página. Quem
/// confirma o pagamento é o webhook.
/// </para>
/// <para>
/// Toda escrita leva chave de idempotência derivada da parcela: repetir (retry, webhook duplicado,
/// gravação que falhou depois do Stripe) devolve o mesmo objeto, nunca cobra, devolve ou repassa duas vezes.
/// </para>
/// </remarks>
internal sealed partial class StripePaymentsGateway(
    IStripeClientProvider clientProvider,
    ILogger<StripePaymentsGateway> logger) : IStripePayments
{
    private const string Currency = Payment.Currency;
    private const string CardType = "card";
    private const string PixType = "pix";

    public string PublishableKey => clientProvider.PublishableKey;

    public async Task<Result<StripeCheckout>> CreateIntentAsync(IntentDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (clientProvider.Client is not { } client)
            return NotConfigured<StripeCheckout>();

        var options = new PaymentIntentCreateOptions
        {
            Amount = draft.AmountCents,
            Currency = Currency,
            PaymentMethodTypes = [CardType, PixType],
            PaymentMethodOptions = new PaymentIntentPaymentMethodOptionsOptions
            {
                Pix = new PaymentIntentPaymentMethodOptionsPixOptions { ExpiresAt = draft.PixExpiresAt.UtcDateTime },
            },
            TransferGroup = TransferGroup(draft.BookingId),
            Description = string.Create(CultureInfo.InvariantCulture, $"PetHost - reserva {draft.BookingId} - parcela {draft.InstallmentNumber}/{draft.InstallmentCount}"),
            Metadata = Metadata(draft.PaymentId, draft.BookingId, draft.InstallmentNumber),
        };

        var requestOptions = new RequestOptions { IdempotencyKey = Key(draft.InstallmentId, $"intent-{draft.Attempt}") };

        try
        {
            var intent = await new PaymentIntentService(client)
                .CreateAsync(options, requestOptions, cancellationToken)
                .ConfigureAwait(false);

            LogIntentCreated(logger, intent.Id, draft.BookingId, draft.InstallmentNumber);
            return Result<StripeCheckout>.Success(new StripeCheckout(intent.Id, intent.ClientSecret, StateOf(intent.Status)));
        }
        catch (StripeException exception)
        {
            return Failure<StripeCheckout>(exception, "create payment intent");
        }
    }

    public async Task<Result<StripeCheckout>> PrepareIntentAsync(string paymentIntentId, DateTimeOffset pixExpiresAt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentIntentId);

        if (clientProvider.Client is not { } client)
            return NotConfigured<StripeCheckout>();

        var service = new PaymentIntentService(client);

        try
        {
            var intent = await service.GetAsync(paymentIntentId, options: null, requestOptions: null, cancellationToken).ConfigureAwait(false);

            // O Pix anterior venceu (ou o cartão foi recusado): renova o prazo do próximo QR code.
            if (intent.Status == "requires_payment_method")
            {
                intent = await service
                    .UpdateAsync(
                        paymentIntentId,
                        new PaymentIntentUpdateOptions
                        {
                            PaymentMethodOptions = new PaymentIntentPaymentMethodOptionsOptions
                            {
                                Pix = new PaymentIntentPaymentMethodOptionsPixOptions { ExpiresAt = pixExpiresAt.UtcDateTime },
                            },
                        },
                        requestOptions: null,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return Result<StripeCheckout>.Success(new StripeCheckout(intent.Id, intent.ClientSecret, StateOf(intent.Status)));
        }
        catch (StripeException exception)
        {
            return Failure<StripeCheckout>(exception, "prepare payment intent");
        }
    }

    public async Task<Result> CancelIntentAsync(string paymentIntentId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentIntentId);

        if (clientProvider.Client is not { } client)
            return NotConfigured<bool>();

        var service = new PaymentIntentService(client);

        try
        {
            var intent = await service.GetAsync(paymentIntentId, options: null, requestOptions: null, cancellationToken).ConfigureAwait(false);

            switch (StateOf(intent.Status))
            {
                case ChargeState.Canceled:
                    return Result.Success();
                case ChargeState.Succeeded or ChargeState.Processing:
                    return Result.Failure(PaymentsErrors.StripeRequestRejected);
            }

            await service.CancelAsync(paymentIntentId, options: null, requestOptions: null, cancellationToken).ConfigureAwait(false);

            LogIntentCanceled(logger, paymentIntentId);
            return Result.Success();
        }
        catch (StripeException exception)
        {
            return Failure<bool>(exception, "cancel payment intent");
        }
    }

    public async Task<Result<ChargeSnapshot>> GetChargeAsync(string paymentIntentId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentIntentId);

        if (clientProvider.Client is not { } client)
            return NotConfigured<ChargeSnapshot>();

        try
        {
            var intent = await new PaymentIntentService(client)
                .GetAsync(paymentIntentId, new PaymentIntentGetOptions { Expand = ["latest_charge"] }, requestOptions: null, cancellationToken)
                .ConfigureAwait(false);

            var charge = intent.LatestCharge;

            RefundSnapshot? refund = null;
            DisputeSnapshot? dispute = null;

            if (charge is not null)
            {
                var refunds = await new RefundService(client)
                    .ListAsync(new RefundListOptions { PaymentIntent = paymentIntentId, Limit = 10 }, requestOptions: null, cancellationToken)
                    .ConfigureAwait(false);

                refund = Summarize(refunds.Data);

                if (charge.Disputed)
                {
                    var disputes = await new DisputeService(client)
                        .ListAsync(new DisputeListOptions { PaymentIntent = paymentIntentId, Limit = 1 }, requestOptions: null, cancellationToken)
                        .ConfigureAwait(false);

                    dispute = disputes.Data.FirstOrDefault() is { } found ? new DisputeSnapshot(found.Id, DisputeOf(found.Status)) : null;
                }
            }

            return Result<ChargeSnapshot>.Success(new ChargeSnapshot(
                StateOf(intent.Status),
                MethodOf(charge?.PaymentMethodDetails?.Type ?? (intent.NextAction?.Type == "pix_display_qr_code" ? PixType : null)),
                charge?.Id,
                intent.LastPaymentError?.DeclineCode ?? intent.LastPaymentError?.Code,
                refund,
                dispute));
        }
        catch (StripeException exception)
        {
            return Failure<ChargeSnapshot>(exception, "read payment intent");
        }
    }

    public async Task<Result<string?>> ResolveIntentIdAsync(string stripeObjectId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stripeObjectId);

        if (clientProvider.Client is not { } client)
            return NotConfigured<string?>();

        try
        {
            string? intentId = stripeObjectId switch
            {
                _ when stripeObjectId.StartsWith("pi_", StringComparison.Ordinal) => stripeObjectId,
                _ when stripeObjectId.StartsWith("ch_", StringComparison.Ordinal) || stripeObjectId.StartsWith("py_", StringComparison.Ordinal) =>
                    (await new ChargeService(client).GetAsync(stripeObjectId, options: null, requestOptions: null, cancellationToken).ConfigureAwait(false)).PaymentIntentId,
                _ when stripeObjectId.StartsWith("re_", StringComparison.Ordinal) || stripeObjectId.StartsWith("pyr_", StringComparison.Ordinal) =>
                    (await new RefundService(client).GetAsync(stripeObjectId, options: null, requestOptions: null, cancellationToken).ConfigureAwait(false)).PaymentIntentId,
                _ when stripeObjectId.StartsWith("dp_", StringComparison.Ordinal) || stripeObjectId.StartsWith("du_", StringComparison.Ordinal) =>
                    (await new DisputeService(client).GetAsync(stripeObjectId, options: null, requestOptions: null, cancellationToken).ConfigureAwait(false)).PaymentIntentId,
                _ => null,
            };

            return Result<string?>.Success(intentId);
        }
        catch (StripeException exception) when (exception.HttpStatusCode == HttpStatusCode.NotFound)
        {
            // Objeto de outra conta ou apagado: não é de uma parcela do PetHost.
            return Result<string?>.Success(null);
        }
        catch (StripeException exception)
        {
            return Failure<string?>(exception, "resolve payment intent");
        }
    }

    public async Task<Result<RefundSnapshot>> RefundAsync(RefundDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (clientProvider.Client is not { } client)
            return NotConfigured<RefundSnapshot>();

        try
        {
            var refund = await new RefundService(client)
                .CreateAsync(
                    new RefundCreateOptions
                    {
                        PaymentIntent = draft.PaymentIntentId,
                        Amount = draft.AmountCents,
                        Reason = "requested_by_customer",
                        Metadata = new Dictionary<string, string>
                        {
                            ["payment_id"] = draft.PaymentId.ToString(),
                            ["installment_number"] = draft.InstallmentNumber.ToString(CultureInfo.InvariantCulture),
                        },
                    },
                    new RequestOptions { IdempotencyKey = Key(draft.InstallmentId, "refund") },
                    cancellationToken)
                .ConfigureAwait(false);

            LogRefundCreated(logger, refund.Id, draft.PaymentIntentId, draft.AmountCents);
            return Result<RefundSnapshot>.Success(new RefundSnapshot(refund.Id, refund.Amount, RefundOf(refund.Status)));
        }
        catch (StripeException exception)
        {
            return Failure<RefundSnapshot>(exception, "create refund");
        }
    }

    public async Task<Result<string>> TransferAsync(TransferDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (clientProvider.Client is not { } client)
            return NotConfigured<string>();

        try
        {
            var transfer = await new TransferService(client)
                .CreateAsync(
                    new TransferCreateOptions
                    {
                        Amount = draft.AmountCents,
                        Currency = Currency,
                        Destination = draft.DestinationAccountId,
                        SourceTransaction = draft.ChargeId,
                        TransferGroup = TransferGroup(draft.BookingId),
                        Description = string.Create(CultureInfo.InvariantCulture, $"PetHost - repasse da reserva {draft.BookingId} - parcela {draft.InstallmentNumber}"),
                        Metadata = Metadata(draft.PaymentId, draft.BookingId, draft.InstallmentNumber),
                    },
                    new RequestOptions { IdempotencyKey = Key(draft.InstallmentId, "transfer") },
                    cancellationToken)
                .ConfigureAwait(false);

            LogTransferCreated(logger, transfer.Id, draft.DestinationAccountId, draft.AmountCents);
            return Result<string>.Success(transfer.Id);
        }
        catch (StripeException exception)
        {
            return Failure<string>(exception, "create transfer");
        }
    }

    public async Task<Result<string>> ReverseTransferAsync(ReversalDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);

        if (clientProvider.Client is not { } client)
            return NotConfigured<string>();

        try
        {
            var reversal = await new TransferReversalService(client)
                .CreateAsync(
                    draft.TransferId,
                    new TransferReversalCreateOptions
                    {
                        Amount = draft.AmountCents,
                        Metadata = new Dictionary<string, string>
                        {
                            ["payment_id"] = draft.PaymentId.ToString(),
                            ["installment_number"] = draft.InstallmentNumber.ToString(CultureInfo.InvariantCulture),
                            ["reason"] = "dispute_lost",
                        },
                    },
                    new RequestOptions { IdempotencyKey = Key(draft.InstallmentId, "reversal") },
                    cancellationToken)
                .ConfigureAwait(false);

            LogTransferReversed(logger, reversal.Id, draft.TransferId, draft.AmountCents);
            return Result<string>.Success(reversal.Id);
        }
        catch (StripeException exception)
        {
            return Failure<string>(exception, "reverse transfer");
        }
    }

    /// <summary>A reserva agrupa as cobranças e os repasses no Dashboard do Stripe.</summary>
    internal static string TransferGroup(Guid bookingId) => $"booking_{bookingId:N}";

    /// <summary>Chave de idempotência da parcela para uma operação.</summary>
    internal static string Key(Guid installmentId, string operation) => $"pethost-installment-{installmentId:N}-{operation}";

    internal static ChargeState StateOf(string? status) => status switch
    {
        "succeeded" => ChargeState.Succeeded,
        "processing" => ChargeState.Processing,
        "canceled" => ChargeState.Canceled,
        _ => ChargeState.AwaitingPayment,
    };

    internal static DomainPaymentMethod? MethodOf(string? type) => type switch
    {
        CardType => DomainPaymentMethod.Card,
        PixType => DomainPaymentMethod.Pix,
        _ => null,
    };

    internal static DomainRefundStatus RefundOf(string? status) => status switch
    {
        "succeeded" => DomainRefundStatus.Succeeded,
        "failed" or "canceled" => DomainRefundStatus.Failed,
        _ => DomainRefundStatus.Pending,
    };

    /// <summary>Encerrada a favor do PetHost (ou consulta encerrada sem chargeback): ganha. <c>lost</c>: perdida. O resto: aberta.</summary>
    internal static DisputeStatus DisputeOf(string? status) => status switch
    {
        "won" or "warning_closed" or "prevented" => DisputeStatus.Won,
        "lost" => DisputeStatus.Lost,
        _ => DisputeStatus.Open,
    };

    /// <summary>
    /// Os reembolsos da cobrança como um só: a soma dos que valem (a caminho ou concluídos) — a
    /// caminho se algum ainda estiver; senão, o último que falhou.
    /// </summary>
    internal static RefundSnapshot? Summarize(IReadOnlyCollection<Refund> refunds)
    {
        var valid = refunds.Where(r => RefundOf(r.Status) != DomainRefundStatus.Failed).ToList();
        if (valid.Count > 0)
        {
            var status = valid.Any(r => RefundOf(r.Status) == DomainRefundStatus.Pending) ? DomainRefundStatus.Pending : DomainRefundStatus.Succeeded;
            return new RefundSnapshot(valid.MaxBy(r => r.Created)!.Id, valid.Sum(r => r.Amount), status);
        }

        return refunds.MaxBy(r => r.Created) is { } failed
            ? new RefundSnapshot(failed.Id, failed.Amount, DomainRefundStatus.Failed)
            : null;
    }

    private static Dictionary<string, string> Metadata(Guid paymentId, Guid bookingId, int installmentNumber) => new()
    {
        ["booking_id"] = bookingId.ToString(),
        ["payment_id"] = paymentId.ToString(),
        ["installment_number"] = installmentNumber.ToString(CultureInfo.InvariantCulture),
    };

    private Result<T> NotConfigured<T>()
    {
        LogNotConfigured(logger);
        return Result<T>.Failure(PaymentsErrors.StripeUnavailable);
    }

    /// <summary>
    /// Pedido recusado pelo Stripe (400, 402, 404) é dado ou estado que ele não aceita: 422. O resto —
    /// fora do ar, lento, limite, chave errada, chave de idempotência reaproveitada — é indisponibilidade: 503.
    /// </summary>
    private Result<T> Failure<T>(StripeException exception, string operation)
    {
        var rejected = exception.HttpStatusCode is HttpStatusCode.BadRequest or HttpStatusCode.PaymentRequired or HttpStatusCode.NotFound
            && exception.StripeError?.Type != "idempotency_error";

        var code = exception.StripeError?.Code ?? exception.StripeError?.Type ?? "none";

        if (rejected)
        {
            LogRejected(logger, operation, (int)exception.HttpStatusCode, code, exception.StripeError?.Param, exception.StripeError?.Message);
            return Result<T>.Failure(PaymentsErrors.StripeRequestRejected);
        }

        LogUnavailable(logger, exception, operation, (int)exception.HttpStatusCode, code);
        return Result<T>.Failure(PaymentsErrors.StripeUnavailable);
    }

    [LoggerMessage(EventId = 9100, Level = LogLevel.Information, Message = "Stripe payment intent {PaymentIntentId} created for booking {BookingId}, installment {InstallmentNumber}.")]
    private static partial void LogIntentCreated(ILogger logger, string paymentIntentId, Guid bookingId, int installmentNumber);

    [LoggerMessage(EventId = 9101, Level = LogLevel.Information, Message = "Stripe payment intent {PaymentIntentId} canceled.")]
    private static partial void LogIntentCanceled(ILogger logger, string paymentIntentId);

    [LoggerMessage(EventId = 9102, Level = LogLevel.Information, Message = "Stripe refund {RefundId} created for {PaymentIntentId} ({AmountCents} cents).")]
    private static partial void LogRefundCreated(ILogger logger, string refundId, string paymentIntentId, long amountCents);

    [LoggerMessage(EventId = 9103, Level = LogLevel.Information, Message = "Stripe transfer {TransferId} created to {StripeAccountId} ({AmountCents} cents).")]
    private static partial void LogTransferCreated(ILogger logger, string transferId, string stripeAccountId, long amountCents);

    [LoggerMessage(EventId = 9104, Level = LogLevel.Information, Message = "Stripe transfer reversal {ReversalId} created for {TransferId} ({AmountCents} cents).")]
    private static partial void LogTransferReversed(ILogger logger, string reversalId, string transferId, long amountCents);

    [LoggerMessage(EventId = 9105, Level = LogLevel.Warning, Message = "Stripe is not configured (Stripe:SecretKey is empty); payments are unavailable.")]
    private static partial void LogNotConfigured(ILogger logger);

    [LoggerMessage(EventId = 9106, Level = LogLevel.Warning, Message = "Stripe rejected {Operation} ({StatusCode}, {ErrorCode}, param {Param}): {StripeMessage}")]
    private static partial void LogRejected(ILogger logger, string operation, int statusCode, string errorCode, string? param, string? stripeMessage);

    [LoggerMessage(EventId = 9107, Level = LogLevel.Error, Message = "Stripe unavailable during {Operation} ({StatusCode}, {ErrorCode}).")]
    private static partial void LogUnavailable(ILogger logger, Exception exception, string operation, int statusCode, string errorCode);
}
