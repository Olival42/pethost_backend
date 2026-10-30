using PetHost.Modules.Payments.Application.Abstractions;
using PetHost.Modules.Payments.Domain.Errors;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Payments.IntegrationTests;

/// <summary>
/// O Stripe em memória: guarda as cobranças, reembolsos, repasses e estornos que a API pede, e o
/// teste faz o papel do tutor e do banco (<see cref="Pay"/>, <see cref="OpenDispute"/>...). O
/// webhook continua real: o teste manda o evento assinado e a API relê aqui.
/// </summary>
public sealed class FakeStripePayments : IStripePayments
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, Intent> _intents = [];
    private readonly Dictionary<string, string> _owners = [];
    private int _sequence;

    public string PublishableKey => "pk_test_fake";

    /// <summary>Repasses pedidos: (conta, valor, cobrança de origem).</summary>
    public List<(string Destination, long AmountCents, string ChargeId)> Transfers { get; } = [];

    /// <summary>Reembolsos pedidos: (cobrança, valor).</summary>
    public List<(string PaymentIntentId, long AmountCents)> Refunds { get; } = [];

    /// <summary>Estornos de repasse pedidos: (transferência, valor).</summary>
    public List<(string TransferId, long AmountCents)> Reversals { get; } = [];

    /// <summary>Cobranças criadas, na ordem.</summary>
    public List<string> CreatedIntents { get; } = [];

    public void Reset()
    {
        lock (_lock)
        {
            _intents.Clear();
            _owners.Clear();
            Transfers.Clear();
            Refunds.Clear();
            Reversals.Clear();
            CreatedIntents.Clear();
        }
    }

    /// <summary>O tutor pagou e o Stripe confirmou.</summary>
    public void Pay(string paymentIntentId, PaymentMethod method = PaymentMethod.Card)
    {
        lock (_lock)
        {
            var intent = _intents[paymentIntentId];
            intent.State = ChargeState.Succeeded;
            intent.Method = method;
            intent.ChargeId = $"ch_fake_{paymentIntentId}";
            _owners[intent.ChargeId] = paymentIntentId;
        }
    }

    /// <summary>O tutor contestou a cobrança no cartão. Devolve o id da contestação.</summary>
    public string OpenDispute(string paymentIntentId)
    {
        lock (_lock)
        {
            var disputeId = $"dp_fake_{paymentIntentId}";
            _intents[paymentIntentId].Dispute = new DisputeSnapshot(disputeId, DisputeStatus.Open);
            _owners[disputeId] = paymentIntentId;
            return disputeId;
        }
    }

    /// <summary>O banco decidiu a contestação.</summary>
    public void CloseDispute(string paymentIntentId, DisputeStatus outcome)
    {
        lock (_lock)
        {
            var intent = _intents[paymentIntentId];
            intent.Dispute = intent.Dispute! with { Status = outcome };
        }
    }

    public ChargeState StateOf(string paymentIntentId)
    {
        lock (_lock)
            return _intents[paymentIntentId].State;
    }

    /// <summary>Quanto a cobrança pede agora (valor da parcela mais multa e juros).</summary>
    public long AmountOf(string paymentIntentId)
    {
        lock (_lock)
            return _intents[paymentIntentId].AmountCents;
    }

    /// <summary>O tutor desistiu do Pix (o QR code venceu): a cobrança volta a esperar o meio de pagamento.</summary>
    public void ExpirePix(string paymentIntentId)
    {
        lock (_lock)
            _intents[paymentIntentId].QrCodeShown = false;
    }

    /// <summary>O tutor abriu o Pix: o QR code fica valendo com o valor de agora.</summary>
    public void ShowPix(string paymentIntentId)
    {
        lock (_lock)
            _intents[paymentIntentId].QrCodeShown = true;
    }

    public Task<Result<StripeCheckout>> CreateIntentAsync(IntentDraft draft, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var id = $"pi_fake_{++_sequence}";
            _intents[id] = new Intent(draft.AmountCents);
            CreatedIntents.Add(id);

            return Task.FromResult(Result<StripeCheckout>.Success(new StripeCheckout(id, $"{id}_secret_fake", ChargeState.AwaitingPayment, draft.AmountCents)));
        }
    }

    public Task<Result<StripeCheckout>> PrepareIntentAsync(string paymentIntentId, long amountCents, DateTimeOffset pixExpiresAt, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            // Como o Stripe: o valor só muda enquanto a cobrança espera o meio de pagamento (sem QR code valendo).
            var intent = _intents[paymentIntentId];
            if (intent.State == ChargeState.AwaitingPayment && !intent.QrCodeShown)
                intent.AmountCents = amountCents;

            return Task.FromResult(Result<StripeCheckout>.Success(new StripeCheckout(paymentIntentId, $"{paymentIntentId}_secret_fake", intent.State, intent.AmountCents)));
        }
    }

    public Task<Result> CancelIntentAsync(string paymentIntentId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var intent = _intents[paymentIntentId];
            if (intent.State is ChargeState.Succeeded or ChargeState.Processing)
                return Task.FromResult(Result.Failure(PaymentsErrors.StripeRequestRejected));

            intent.State = ChargeState.Canceled;
            return Task.FromResult(Result.Success());
        }
    }

    public Task<Result<ChargeSnapshot>> GetChargeAsync(string paymentIntentId, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var intent = _intents[paymentIntentId];
            return Task.FromResult(Result<ChargeSnapshot>.Success(new ChargeSnapshot(intent.State, intent.Method, intent.ChargeId, null, intent.Refund, intent.Dispute, intent.AmountCents)));
        }
    }

    public Task<Result<string?>> ResolveIntentIdAsync(string stripeObjectId, CancellationToken cancellationToken)
    {
        lock (_lock)
            return Task.FromResult(Result<string?>.Success(_owners.GetValueOrDefault(stripeObjectId)));
    }

    public Task<Result<RefundSnapshot>> RefundAsync(RefundDraft draft, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            var refund = new RefundSnapshot($"re_fake_{draft.PaymentIntentId}", draft.AmountCents, RefundStatus.Succeeded);
            _intents[draft.PaymentIntentId].Refund = refund;
            _owners[refund.Id] = draft.PaymentIntentId;
            Refunds.Add((draft.PaymentIntentId, draft.AmountCents));

            return Task.FromResult(Result<RefundSnapshot>.Success(refund));
        }
    }

    public Task<Result<string>> TransferAsync(TransferDraft draft, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            Transfers.Add((draft.DestinationAccountId, draft.AmountCents, draft.ChargeId));
            return Task.FromResult(Result<string>.Success($"tr_fake_{Transfers.Count}"));
        }
    }

    public Task<Result<string>> ReverseTransferAsync(ReversalDraft draft, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            Reversals.Add((draft.TransferId, draft.AmountCents));
            return Task.FromResult(Result<string>.Success($"trr_fake_{Reversals.Count}"));
        }
    }

    private sealed class Intent(long amountCents)
    {
        public long AmountCents { get; set; } = amountCents;
        public bool QrCodeShown { get; set; }
        public ChargeState State { get; set; } = ChargeState.AwaitingPayment;
        public PaymentMethod? Method { get; set; }
        public string? ChargeId { get; set; }
        public RefundSnapshot? Refund { get; set; }
        public DisputeSnapshot? Dispute { get; set; }
    }
}
