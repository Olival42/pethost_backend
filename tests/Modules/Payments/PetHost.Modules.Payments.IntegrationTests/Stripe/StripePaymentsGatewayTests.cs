using System.Collections.Concurrent;
using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PetHost.Modules.Payments.Application.Abstractions;
using PetHost.Modules.Payments.Domain.Errors;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Modules.Payments.Infrastructure.Stripe;
using PetHost.Shared.Infrastructure.Stripe;
using Stripe;
using Xunit;
using DomainPaymentMethod = PetHost.Modules.Payments.Domain.Payments.PaymentMethod;
using DomainRefundStatus = PetHost.Modules.Payments.Domain.Payments.RefundStatus;

namespace PetHost.Modules.Payments.IntegrationTests.Stripe;

/// <summary>
/// O adaptador do Stripe dos pagamentos com o SDK de verdade, falando por HTTP com um Stripe de
/// teste (o resto dos testes de pagamento troca esta porta por um falso): o que cada operação manda
/// (valor, moeda, meios, metadados, chave de idempotência), como lê a resposta e como traduz os erros
/// do Stripe em <c>PAYMENT_STRIPE_REQUEST_REJECTED</c> ou <c>PAYMENT_STRIPE_UNAVAILABLE</c>.
/// </summary>
public sealed class StripePaymentsGatewayTests : IDisposable
{
    private static readonly Guid InstallmentId = Guid.Parse("0199b000-0000-7000-8000-000000000001");
    private static readonly Guid PaymentId = Guid.Parse("0199b000-0000-7000-8000-000000000002");
    private static readonly Guid BookingId = Guid.Parse("0199b000-0000-7000-8000-000000000003");
    private static readonly DateTimeOffset PixExpiresAt = new(2026, 12, 2, 15, 0, 0, TimeSpan.Zero);

    private readonly StripeStub _stripe = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _stripe.Dispose();

    private StripePaymentsGateway Gateway(bool configured = true) =>
        new(configured ? _stripe.Provider() : new NotConfiguredProvider(), NullLogger<StripePaymentsGateway>.Instance);

    // ----- cobrança -----

    [Fact]
    public async Task CreateIntentAsync_Should_SendCardAndPixWithTheMetadataAndAnIdempotencyKey_When_Called()
    {
        // Arrange
        _stripe.On("POST", "/v1/payment_intents", Intent("pi_1", "requires_payment_method", 13_534));

        // Act
        var result = await Gateway().CreateIntentAsync(
            new IntentDraft(InstallmentId, PaymentId, BookingId, 2, 3, 1, 13_534, PixExpiresAt, LateFeeCents: 201),
            Ct);

        // Assert
        result.Value.Should().Be(new StripeCheckout("pi_1", "pi_1_secret_x", ChargeState.AwaitingPayment, 13_534));
        var request = _stripe.Single("POST", "/v1/payment_intents");
        request.Form.Should().Contain("amount=13534").And.Contain("currency=brl")
            .And.Contain("payment_method_types[0]=card").And.Contain("payment_method_types[1]=pix")
            .And.Contain($"transfer_group=booking_{BookingId:N}")
            .And.Contain($"metadata[booking_id]={BookingId}").And.Contain("metadata[installment_number]=2")
            .And.Contain("metadata[late_fee_cents]=201");
        request.IdempotencyKey.Should().Be($"pethost-installment-{InstallmentId:N}-intent-1");
    }

    [Fact]
    public async Task CreateIntentAsync_Should_LeaveTheLateFeeOut_When_TheInstallmentIsOnTime()
    {
        // Arrange
        _stripe.On("POST", "/v1/payment_intents", Intent("pi_1", "requires_payment_method", 13_333));

        // Act
        await Gateway().CreateIntentAsync(new IntentDraft(InstallmentId, PaymentId, BookingId, 1, 3, 1, 13_333, PixExpiresAt), Ct);

        // Assert
        _stripe.Single("POST", "/v1/payment_intents").Form.Should().NotContain("late_fee_cents");
    }

    [Fact]
    public async Task PrepareIntentAsync_Should_UpdateTheAmountAndThePixDeadline_When_TheIntentWaitsForAPaymentMethod()
    {
        // Arrange
        _stripe.On("GET", "/v1/payment_intents/pi_1", Intent("pi_1", "requires_payment_method", 13_333));
        _stripe.On("POST", "/v1/payment_intents/pi_1", Intent("pi_1", "requires_payment_method", 13_534));

        // Act
        var result = await Gateway().PrepareIntentAsync("pi_1", 13_534, PixExpiresAt, Ct);

        // Assert
        result.Value!.AmountCents.Should().Be(13_534);
        _stripe.Single("POST", "/v1/payment_intents/pi_1").Form.Should().Contain("amount=13534");
    }

    [Fact]
    public async Task PrepareIntentAsync_Should_KeepThePixAsItIs_When_ItsQrCodeIsStillValid()
    {
        // Arrange
        _stripe.On("GET", "/v1/payment_intents/pi_1", Intent("pi_1", "requires_action", 13_333));

        // Act
        var result = await Gateway().PrepareIntentAsync("pi_1", 13_534, PixExpiresAt, Ct);

        // Assert
        result.Value!.AmountCents.Should().Be(13_333);
        _stripe.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("canceled", false, true)]
    [InlineData("requires_payment_method", true, true)]
    [InlineData("succeeded", false, false)]
    [InlineData("processing", false, false)]
    public async Task CancelIntentAsync_Should_CancelOnlyAnOpenIntent_When_Called(string status, bool expectCancelCall, bool expectSuccess)
    {
        // Arrange
        _stripe.On("GET", "/v1/payment_intents/pi_1", Intent("pi_1", status, 13_333));
        _stripe.On("POST", "/v1/payment_intents/pi_1/cancel", Intent("pi_1", "canceled", 13_333));

        // Act
        var result = await Gateway().CancelIntentAsync("pi_1", Ct);

        // Assert
        result.IsSuccess.Should().Be(expectSuccess);
        if (!expectSuccess)
            result.FirstError.Should().Be(PaymentsErrors.StripeRequestRejected);
        _stripe.Requests.Any(r => r.Path == "/v1/payment_intents/pi_1/cancel").Should().Be(expectCancelCall);
    }

    [Fact]
    public async Task GetChargeAsync_Should_ReadTheChargeTheRefundsAndTheDispute_When_TheCardWasDisputed()
    {
        // Arrange
        _stripe.On("GET", "/v1/payment_intents/pi_1", """
            { "id": "pi_1", "object": "payment_intent", "status": "succeeded", "amount": 13333, "amount_received": 13333, "currency": "brl",
              "latest_charge": { "id": "ch_1", "object": "charge", "disputed": true, "amount": 13333,
                                 "payment_method_details": { "type": "card" } } }
            """);
        _stripe.On("GET", "/v1/refunds", """
            { "object": "list", "has_more": false, "url": "/v1/refunds", "data": [
              { "id": "re_old", "object": "refund", "amount": 1000, "status": "failed", "created": 1700000000 },
              { "id": "re_new", "object": "refund", "amount": 5000, "status": "succeeded", "created": 1700000100 } ] }
            """);
        _stripe.On("GET", "/v1/disputes", """
            { "object": "list", "has_more": false, "url": "/v1/disputes", "data": [ { "id": "dp_1", "object": "dispute", "status": "lost" } ] }
            """);

        // Act
        var result = await Gateway().GetChargeAsync("pi_1", Ct);

        // Assert
        result.Value.Should().Be(new ChargeSnapshot(
            ChargeState.Succeeded,
            DomainPaymentMethod.Card,
            "ch_1",
            null,
            new RefundSnapshot("re_new", 5000, DomainRefundStatus.Succeeded),
            new DisputeSnapshot("dp_1", DisputeStatus.Lost),
            13_333));
        _stripe.Single("GET", "/v1/payment_intents/pi_1").Query.Should().Contain("expand[0]=latest_charge");
    }

    [Fact]
    public async Task GetChargeAsync_Should_ReportPixAndTheDeclineCode_When_TheQrCodeIsShownWithoutACharge()
    {
        // Arrange
        _stripe.On("GET", "/v1/payment_intents/pi_1", """
            { "id": "pi_1", "object": "payment_intent", "status": "requires_action", "amount": 13333, "currency": "brl",
              "next_action": { "type": "pix_display_qr_code" },
              "last_payment_error": { "type": "card_error", "code": "card_declined", "decline_code": "insufficient_funds" } }
            """);

        // Act
        var result = await Gateway().GetChargeAsync("pi_1", Ct);

        // Assert
        result.Value!.State.Should().Be(ChargeState.AwaitingPayment);
        result.Value.Method.Should().Be(DomainPaymentMethod.Pix);
        result.Value.FailureCode.Should().Be("insufficient_funds");
        result.Value.Refund.Should().BeNull();
        _stripe.Requests.Should().ContainSingle("sem cobrança, não há reembolso nem contestação para ler");
    }

    // ----- objeto do webhook -----

    [Theory]
    [InlineData("ch_1", "/v1/charges/ch_1", """{ "id": "ch_1", "object": "charge", "payment_intent": "pi_9" }""")]
    [InlineData("re_1", "/v1/refunds/re_1", """{ "id": "re_1", "object": "refund", "payment_intent": "pi_9" }""")]
    [InlineData("dp_1", "/v1/disputes/dp_1", """{ "id": "dp_1", "object": "dispute", "payment_intent": "pi_9" }""")]
    public async Task ResolveIntentIdAsync_Should_FindTheIntentOfTheObject_When_ItIsAChargeRefundOrDispute(string objectId, string path, string json)
    {
        // Arrange
        _stripe.On("GET", path, json);

        // Act
        var result = await Gateway().ResolveIntentIdAsync(objectId, Ct);

        // Assert
        result.Value.Should().Be("pi_9");
    }

    [Theory]
    [InlineData("pi_7", "pi_7")]
    [InlineData("acct_1", null)]
    public async Task ResolveIntentIdAsync_Should_AnswerWithoutCallingStripe_When_ItIsAnIntentOrAnotherObject(string objectId, string? expected)
    {
        // Act
        var result = await Gateway().ResolveIntentIdAsync(objectId, Ct);

        // Assert
        result.Value.Should().Be(expected);
        _stripe.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveIntentIdAsync_Should_ReturnNull_When_StripeDoesNotKnowTheObject()
    {
        // Arrange
        _stripe.On("GET", "/v1/charges/ch_x", Error("invalid_request_error", "resource_missing"), HttpStatusCode.NotFound);

        // Act
        var result = await Gateway().ResolveIntentIdAsync("ch_x", Ct);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    // ----- reembolso, repasse e estorno -----

    [Fact]
    public async Task RefundAsync_Should_RefundTheIntentWithTheInstallmentKey_When_Called()
    {
        // Arrange
        _stripe.On("POST", "/v1/refunds", """{ "id": "re_1", "object": "refund", "amount": 6667, "status": "pending" }""");

        // Act
        var result = await Gateway().RefundAsync(new RefundDraft(InstallmentId, PaymentId, 1, "pi_1", 6667), Ct);

        // Assert
        result.Value.Should().Be(new RefundSnapshot("re_1", 6667, DomainRefundStatus.Pending));
        var request = _stripe.Single("POST", "/v1/refunds");
        request.Form.Should().Contain("payment_intent=pi_1").And.Contain("amount=6667").And.Contain("reason=requested_by_customer");
        request.IdempotencyKey.Should().Be($"pethost-installment-{InstallmentId:N}-refund");
    }

    [Fact]
    public async Task TransferAsync_Should_SendTheHostShareTiedToTheCharge_When_Called()
    {
        // Arrange
        _stripe.On("POST", "/v1/transfers", """{ "id": "tr_1", "object": "transfer", "amount": 12000 }""");

        // Act
        var result = await Gateway().TransferAsync(new TransferDraft(InstallmentId, PaymentId, BookingId, 1, "ch_1", "acct_host", 12_000), Ct);

        // Assert
        result.Value.Should().Be("tr_1");
        var request = _stripe.Single("POST", "/v1/transfers");
        request.Form.Should().Contain("amount=12000").And.Contain("currency=brl").And.Contain("destination=acct_host")
            .And.Contain("source_transaction=ch_1").And.Contain($"transfer_group=booking_{BookingId:N}");
        request.IdempotencyKey.Should().Be($"pethost-installment-{InstallmentId:N}-transfer");
    }

    [Fact]
    public async Task ReverseTransferAsync_Should_ReverseTheTransferForTheLostDispute_When_Called()
    {
        // Arrange
        _stripe.On("POST", "/v1/transfers/tr_1/reversals", """{ "id": "trr_1", "object": "transfer_reversal", "amount": 12000, "transfer": "tr_1" }""");

        // Act
        var result = await Gateway().ReverseTransferAsync(new ReversalDraft(InstallmentId, PaymentId, 1, "tr_1", 12_000), Ct);

        // Assert
        result.Value.Should().Be("trr_1");
        var request = _stripe.Single("POST", "/v1/transfers/tr_1/reversals");
        request.Form.Should().Contain("amount=12000").And.Contain("metadata[reason]=dispute_lost");
        request.IdempotencyKey.Should().Be($"pethost-installment-{InstallmentId:N}-reversal");
    }

    // ----- erros -----

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "invalid_request_error", "PAYMENT_STRIPE_REQUEST_REJECTED")]
    [InlineData(HttpStatusCode.PaymentRequired, "card_error", "PAYMENT_STRIPE_REQUEST_REJECTED")]
    [InlineData(HttpStatusCode.BadRequest, "idempotency_error", "PAYMENT_STRIPE_UNAVAILABLE")]
    [InlineData(HttpStatusCode.InternalServerError, "api_error", "PAYMENT_STRIPE_UNAVAILABLE")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate_limit_error", "PAYMENT_STRIPE_UNAVAILABLE")]
    public async Task Operations_Should_TellRejectedFromUnavailable_When_StripeAnswersAnError(HttpStatusCode status, string type, string code)
    {
        // Arrange: o mesmo erro em todas as rotas.
        _stripe.Fallback = (Error(type, "x"), status);
        var gateway = Gateway();

        // Act
        var results = new List<string?>
        {
            (await gateway.CreateIntentAsync(new IntentDraft(InstallmentId, PaymentId, BookingId, 1, 1, 1, 100, PixExpiresAt), Ct)).FirstError?.Code,
            (await gateway.PrepareIntentAsync("pi_1", 100, PixExpiresAt, Ct)).FirstError?.Code,
            (await gateway.CancelIntentAsync("pi_1", Ct)).FirstError?.Code,
            (await gateway.GetChargeAsync("pi_1", Ct)).FirstError?.Code,
            (await gateway.RefundAsync(new RefundDraft(InstallmentId, PaymentId, 1, "pi_1", 100), Ct)).FirstError?.Code,
            (await gateway.TransferAsync(new TransferDraft(InstallmentId, PaymentId, BookingId, 1, "ch_1", "acct_host", 100), Ct)).FirstError?.Code,
            (await gateway.ReverseTransferAsync(new ReversalDraft(InstallmentId, PaymentId, 1, "tr_1", 100), Ct)).FirstError?.Code,
        };
        if (status != HttpStatusCode.NotFound)
            results.Add((await gateway.ResolveIntentIdAsync("re_1", Ct)).FirstError?.Code);

        // Assert
        results.Should().AllBe(code);
    }

    [Fact]
    public async Task Operations_Should_ReturnUnavailableWithoutCallingStripe_When_StripeIsNotConfigured()
    {
        // Arrange
        var gateway = Gateway(configured: false);

        // Act
        var results = new[]
        {
            (await gateway.CreateIntentAsync(new IntentDraft(InstallmentId, PaymentId, BookingId, 1, 1, 1, 100, PixExpiresAt), Ct)).FirstError,
            (await gateway.PrepareIntentAsync("pi_1", 100, PixExpiresAt, Ct)).FirstError,
            (await gateway.CancelIntentAsync("pi_1", Ct)).FirstError,
            (await gateway.GetChargeAsync("pi_1", Ct)).FirstError,
            (await gateway.ResolveIntentIdAsync("ch_1", Ct)).FirstError,
            (await gateway.RefundAsync(new RefundDraft(InstallmentId, PaymentId, 1, "pi_1", 100), Ct)).FirstError,
            (await gateway.TransferAsync(new TransferDraft(InstallmentId, PaymentId, BookingId, 1, "ch_1", "acct_host", 100), Ct)).FirstError,
            (await gateway.ReverseTransferAsync(new ReversalDraft(InstallmentId, PaymentId, 1, "tr_1", 100), Ct)).FirstError,
        };

        // Assert
        results.Should().AllBeEquivalentTo(PaymentsErrors.StripeUnavailable);
        _stripe.Requests.Should().BeEmpty();
    }

    // ----- leitura dos estados -----

    [Theory]
    [InlineData("succeeded", ChargeState.Succeeded)]
    [InlineData("processing", ChargeState.Processing)]
    [InlineData("canceled", ChargeState.Canceled)]
    [InlineData("requires_action", ChargeState.AwaitingPayment)]
    [InlineData(null, ChargeState.AwaitingPayment)]
    public void StateOf_Should_MapTheIntentStatus(string? status, ChargeState expected) =>
        StripePaymentsGateway.StateOf(status).Should().Be(expected);

    [Theory]
    [InlineData("won", DisputeStatus.Won)]
    [InlineData("warning_closed", DisputeStatus.Won)]
    [InlineData("prevented", DisputeStatus.Won)]
    [InlineData("lost", DisputeStatus.Lost)]
    [InlineData("needs_response", DisputeStatus.Open)]
    public void DisputeOf_Should_MapTheDisputeStatus(string status, DisputeStatus expected) =>
        StripePaymentsGateway.DisputeOf(status).Should().Be(expected);

    [Theory]
    [InlineData("succeeded", DomainRefundStatus.Succeeded)]
    [InlineData("failed", DomainRefundStatus.Failed)]
    [InlineData("canceled", DomainRefundStatus.Failed)]
    [InlineData("pending", DomainRefundStatus.Pending)]
    public void RefundOf_Should_MapTheRefundStatus(string status, DomainRefundStatus expected) =>
        StripePaymentsGateway.RefundOf(status).Should().Be(expected);

    [Fact]
    public void Summarize_Should_AddTheValidRefundsAndStayPending_When_OneIsStillPending()
    {
        var summary = StripePaymentsGateway.Summarize(
        [
            new Refund { Id = "re_1", Amount = 1000, Status = "succeeded", Created = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Refund { Id = "re_2", Amount = 500, Status = "pending", Created = new DateTime(2026, 12, 2, 0, 0, 0, DateTimeKind.Utc) },
            new Refund { Id = "re_3", Amount = 900, Status = "failed", Created = new DateTime(2026, 12, 3, 0, 0, 0, DateTimeKind.Utc) },
        ]);

        summary.Should().Be(new RefundSnapshot("re_2", 1500, DomainRefundStatus.Pending));
    }

    [Fact]
    public void Summarize_Should_ReportTheLatestFailure_When_EveryRefundFailed()
    {
        var summary = StripePaymentsGateway.Summarize(
        [
            new Refund { Id = "re_1", Amount = 1000, Status = "failed", Created = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc) },
            new Refund { Id = "re_2", Amount = 1000, Status = "canceled", Created = new DateTime(2026, 12, 2, 0, 0, 0, DateTimeKind.Utc) },
        ]);

        summary.Should().Be(new RefundSnapshot("re_2", 1000, DomainRefundStatus.Failed));
        StripePaymentsGateway.Summarize([]).Should().BeNull();
    }

    private static string Intent(string id, string status, long amount) =>
        $$"""{ "id": "{{id}}", "object": "payment_intent", "status": "{{status}}", "amount": {{amount}}, "currency": "brl", "client_secret": "{{id}}_secret_x" }""";

    private static string Error(string type, string code) =>
        $$"""{ "error": { "type": "{{type}}", "code": "{{code}}", "message": "Erro de teste do Stripe." } }""";

    /// <summary>O Stripe de teste: responde o que o teste configurou por método e caminho e guarda os pedidos.</summary>
    private sealed class StripeStub : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<(string Method, string Path), (string Json, HttpStatusCode Status)> _routes = new();

        public ConcurrentQueue<Request> Requests { get; } = new();

        /// <summary>Resposta de toda rota não configurada.</summary>
        public (string Json, HttpStatusCode Status) Fallback { get; set; } =
            ("""{ "error": { "type": "invalid_request_error", "message": "Rota desconhecida no Stripe de teste." } }""", HttpStatusCode.NotFound);

        public void On(string method, string path, string json, HttpStatusCode status = HttpStatusCode.OK) =>
            _routes[(method, path)] = (json, status);

        public Request Single(string method, string path) => Requests.Should().ContainSingle(r => r.Method == method && r.Path == path).Subject;

        public StubProvider Provider() => new(this);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var key = request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.FirstOrDefault() : null;
            var path = request.RequestUri!.AbsolutePath;
            Requests.Enqueue(new Request(
                request.Method.Method,
                path,
                Uri.UnescapeDataString(request.RequestUri.Query),
                Uri.UnescapeDataString(body.Replace('+', ' ')),
                key));

            var (json, status) = _routes.TryGetValue((request.Method.Method, path), out var route) ? route : Fallback;
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed record Request(string Method, string Path, string Query, string Form, string? IdempotencyKey);

    private sealed class StubProvider(StripeStub stub) : IStripeClientProvider
    {
        public IStripeClient? Client { get; } = new StripeClient(
            "sk_test_pethost_gateway",
            httpClient: new SystemNetHttpClient(new HttpClient(stub), maxNetworkRetries: 0));

        public string PublishableKey => "pk_test_pethost_gateway";
    }

    private sealed class NotConfiguredProvider : IStripeClientProvider
    {
        public IStripeClient? Client => null;

        public string PublishableKey => string.Empty;
    }
}
