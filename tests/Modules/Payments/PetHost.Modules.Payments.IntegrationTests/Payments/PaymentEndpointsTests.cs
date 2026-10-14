using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Bookings.Application.Bookings.ExpireOverdueBookings;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Modules.Hosts.Infrastructure.Persistence;
using PetHost.Modules.Payments.Application.Payments.RunPaymentsMaintenance;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Kernel.Messaging;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Payments.IntegrationTests.Payments;

/// <summary>
/// Pagamento ponta a ponta pela API: parcelas, checkout transparente, confirmação por webhook
/// assinado, repasse no check-out, reembolso no cancelamento, parcela vencida antes e depois do
/// check-in e contestação. O Stripe é o dublê em memória da fixture; o resto é real.
/// </summary>
[Collection(PaymentsApiCollection.Name)]
public sealed class PaymentEndpointsTests(PaymentsApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string BookingsRoute = "/api/v1/bookings";
    private const string Password = "Nova@Senha123";

    private static readonly string[] DogsAndCats = ["dog", "cat"];
    private static readonly string[] EveryDay = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    private HttpClient _client = null!;
    private string _hostAccountId = string.Empty;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateOnly Today => BookingClock.Today(DateTimeOffset.UtcNow);

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        _client = fixture.CreateClient();
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    // ----- o caminho inteiro -----

    [Fact]
    public async Task Payment_Should_ConfirmByWebhook_TransferOnCheckOut_AndTransferLaterInstallmentsWhenPaid()
    {
        var stay = await ArrangeAsync();

        // 4 noites × R$ 60,50 = R$ 242,00 → até 4 parcelas de R$ 50+.
        var bookingId = await RequestAndAcceptAsync(stay, nights: 4);

        var before = Data(await ReadAsync(await SendAsync(HttpMethod.Get, PaymentRoute(bookingId), stay.OwnerToken)));
        before.GetProperty("status").GetString().Should().Be("not_started");
        before.GetProperty("totalAmount").GetDecimal().Should().Be(242m);
        before.GetProperty("options").GetArrayLength().Should().Be(4);
        before.TryGetProperty("paymentDeadline", out _).Should().BeTrue();

        var plan = await SendAsync(HttpMethod.Put, PaymentRoute(bookingId), stay.OwnerToken, new { installmentCount = 2 });
        plan.StatusCode.Should().Be(HttpStatusCode.OK);
        var installments = Data(await ReadAsync(plan)).GetProperty("installments");
        installments.EnumerateArray().Select(i => i.GetProperty("amount").GetDecimal()).Should().Equal(121m, 121m);

        // Checkout transparente da 1ª parcela.
        var session = Data(await ReadAsync(await SendAsync(HttpMethod.Post, SessionRoute(bookingId, 1), stay.OwnerToken)));
        var intent = session.GetProperty("paymentIntentId").GetString()!;
        session.GetProperty("clientSecret").GetString().Should().EndWith("_secret_fake");
        session.GetProperty("publishableKey").GetString().Should().Be("pk_test_fake");
        session.GetProperty("paymentMethods").EnumerateArray().Select(m => m.GetString()).Should().Equal("card", "pix");

        // O tutor paga com Pix; o Stripe avisa pelo webhook.
        fixture.Stripe.Pay(intent, PaymentMethod.Pix);
        var webhook = await SendWebhookAsync("payment_intent.succeeded", intent, "payment_intent", "evt_paid_1");
        webhook.StatusCode.Should().Be(HttpStatusCode.OK);

        Status(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{bookingId}", stay.OwnerToken))).Should().Be("confirmed");
        var afterPay = Data(await ReadAsync(await SendAsync(HttpMethod.Get, PaymentRoute(bookingId), stay.OwnerToken)));
        afterPay.GetProperty("status").GetString().Should().Be("active");
        afterPay.GetProperty("paidAmount").GetDecimal().Should().Be(121m);
        afterPay.GetProperty("installments")[0].GetProperty("method").GetString().Should().Be("pix");

        // O mesmo evento de novo não muda nada.
        (await SendWebhookAsync("payment_intent.succeeded", intent, "payment_intent", "evt_paid_1")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Check-in e check-out: repasse de 90% da parcela paga para a conta conectada da anfitriã.
        (await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/check-in", stay.HostToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/check-out", stay.HostToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        fixture.Stripe.Transfers.Should().Equal((_hostAccountId, 10_890L, $"ch_fake_{intent}"));

        // A 2ª parcela, paga depois da estadia, é repassada assim que é paga.
        var second = Data(await ReadAsync(await SendAsync(HttpMethod.Post, SessionRoute(bookingId, 2), stay.OwnerToken))).GetProperty("paymentIntentId").GetString()!;
        fixture.Stripe.Pay(second);
        (await SendWebhookAsync("payment_intent.succeeded", second, "payment_intent")).StatusCode.Should().Be(HttpStatusCode.OK);

        fixture.Stripe.Transfers.Should().HaveCount(2);
        var hostView = Data(await ReadAsync(await SendAsync(HttpMethod.Get, PaymentRoute(bookingId), stay.HostToken)));
        hostView.GetProperty("status").GetString().Should().Be("paid");
        hostView.GetProperty("payout").GetProperty("transferred").GetDecimal().Should().Be(217.80m);
        hostView.TryGetProperty("options", out _).Should().BeFalse();

        var earnings = Data(await ReadAsync(await SendAsync(HttpMethod.Get, "/api/v1/payments/payouts/me", stay.HostToken)));
        earnings.GetProperty("totalCount").GetInt64().Should().Be(1);
        earnings.GetProperty("items")[0].GetProperty("payout").GetProperty("transferred").GetDecimal().Should().Be(217.80m);

        var paidEmails = await WaitForEmailsAsync(e => e.Subject.Contains("pagamento recebido", StringComparison.Ordinal), 2);
        paidEmails.Should().OnlyContain(e => e.ToAddress == "camila@exemplo.com");
    }

    // ----- plano e checkout -----

    [Fact]
    public async Task Plan_Should_Return400_When_InstallmentCountIsOutOfRange()
    {
        var stay = await ArrangeAsync();
        var bookingId = await RequestAndAcceptAsync(stay, nights: 4);

        var response = await SendAsync(HttpMethod.Put, PaymentRoute(bookingId), stay.OwnerToken, new { installmentCount = 7 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Equal("installmentCount");
    }

    [Fact]
    public async Task Plan_Should_Return422_When_InstallmentsWouldBeBelowFiftyReais()
    {
        var stay = await ArrangeAsync();

        // 2 noites × R$ 60,50 = R$ 121,00 → no máximo 2 parcelas.
        var bookingId = await RequestAndAcceptAsync(stay, nights: 2);

        var response = await SendAsync(HttpMethod.Put, PaymentRoute(bookingId), stay.OwnerToken, new { installmentCount = 3 });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("PAYMENT_INSTALLMENT_COUNT_NOT_ALLOWED");
    }

    [Fact]
    public async Task Plan_Should_Return422_When_BookingWasNotAcceptedYet()
    {
        var stay = await ArrangeAsync();
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId, Today, nights: 2);

        var response = await SendAsync(HttpMethod.Put, PaymentRoute(bookingId), stay.OwnerToken, new { installmentCount = 1 });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("PAYMENT_BOOKING_NOT_AWAITING_PAYMENT");
    }

    [Fact]
    public async Task Plan_Should_CancelTheOldChargeAndReplaceThePlan_When_TheOwnerChangesItBeforePaying()
    {
        var stay = await ArrangeAsync();
        var bookingId = await RequestAndAcceptAsync(stay, nights: 4);
        await SendAsync(HttpMethod.Put, PaymentRoute(bookingId), stay.OwnerToken, new { installmentCount = 2 });
        var oldIntent = Data(await ReadAsync(await SendAsync(HttpMethod.Post, SessionRoute(bookingId, 1), stay.OwnerToken))).GetProperty("paymentIntentId").GetString()!;

        var changed = await SendAsync(HttpMethod.Put, PaymentRoute(bookingId), stay.OwnerToken, new { installmentCount = 1 });

        changed.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(await ReadAsync(changed)).GetProperty("installmentCount").GetInt32().Should().Be(1);
        fixture.Stripe.StateOf(oldIntent).Should().Be(ChargeState.Canceled);
    }

    [Fact]
    public async Task Session_Should_Return422_When_AnEarlierInstallmentIsOpen()
    {
        var stay = await ArrangeAsync();
        var bookingId = await RequestAndAcceptAsync(stay, nights: 4);
        await SendAsync(HttpMethod.Put, PaymentRoute(bookingId), stay.OwnerToken, new { installmentCount = 2 });

        var response = await SendAsync(HttpMethod.Post, SessionRoute(bookingId, 2), stay.OwnerToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("PAYMENT_INSTALLMENT_OUT_OF_ORDER");
    }

    [Fact]
    public async Task Payment_Should_Return404_When_AnotherOwnerLooks_And403_When_TheHostTriesToChooseThePlan()
    {
        var stay = await ArrangeAsync();
        var bookingId = await RequestAndAcceptAsync(stay, nights: 2);
        var stranger = await RegisterOwnerAsync("outra@exemplo.com", "111.444.777-35", "Outra Tutora");

        var seenByStranger = await SendAsync(HttpMethod.Get, PaymentRoute(bookingId), stranger);
        var chosenByHost = await SendAsync(HttpMethod.Put, PaymentRoute(bookingId), stay.HostToken, new { installmentCount = 1 });

        seenByStranger.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(seenByStranger)).Should().Be("PAYMENT_BOOKING_NOT_FOUND");
        chosenByHost.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Synchronization_Should_ConfirmTheBookingWithoutTheWebhook_When_TheOwnerComesBackFromTheCheckout()
    {
        var stay = await ArrangeAsync();
        var bookingId = await RequestAndAcceptAsync(stay, nights: 2);
        await SendAsync(HttpMethod.Put, PaymentRoute(bookingId), stay.OwnerToken, new { installmentCount = 1 });
        var intent = Data(await ReadAsync(await SendAsync(HttpMethod.Post, SessionRoute(bookingId, 1), stay.OwnerToken))).GetProperty("paymentIntentId").GetString()!;
        fixture.Stripe.Pay(intent);

        var synced = await SendAsync(HttpMethod.Post, $"{PaymentRoute(bookingId)}/synchronization", stay.OwnerToken);

        synced.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(await ReadAsync(synced)).GetProperty("status").GetString().Should().Be("paid");
        Status(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{bookingId}", stay.OwnerToken))).Should().Be("confirmed");
    }

    // ----- cancelamento e reembolso -----

    [Fact]
    public async Task Cancellation_Should_RefundEverythingAndCancelTheRest_When_TheHostCancels()
    {
        var stay = await ArrangeAsync();
        var bookingId = await RequestAndAcceptAsync(stay, nights: 4, checkIn: Today.AddDays(10));
        await SendAsync(HttpMethod.Put, PaymentRoute(bookingId), stay.OwnerToken, new { installmentCount = 2 });
        var intent = await PayInstallmentAsync(stay, bookingId, 1);

        var cancelled = await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/cancellation", stay.HostToken);

        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);
        fixture.Stripe.Refunds.Should().Equal((intent, 12_100L));
        fixture.Stripe.Transfers.Should().BeEmpty();
        var payment = Data(await ReadAsync(await SendAsync(HttpMethod.Get, PaymentRoute(bookingId), stay.OwnerToken)));
        payment.GetProperty("status").GetString().Should().Be("cancelled");
        payment.GetProperty("refundPercentage").GetInt32().Should().Be(100);
        payment.GetProperty("refundedAmount").GetDecimal().Should().Be(121m);
        payment.GetProperty("installments")[1].GetProperty("status").GetString().Should().Be("cancelled");
        (await WaitForEmailsAsync(e => e.Subject.Contains("reembolso", StringComparison.Ordinal), 1)).Single().ToAddress.Should().Be("camila@exemplo.com");
    }

    [Fact]
    public async Task Cancellation_Should_RefundHalfAndTransferTheHostShareOfTheRest_When_TheOwnerCancelsOnTheDay()
    {
        var stay = await ArrangeAsync();
        var bookingId = await RequestAndAcceptAsync(stay, nights: 2);
        await SendAsync(HttpMethod.Put, PaymentRoute(bookingId), stay.OwnerToken, new { installmentCount = 1 });
        var intent = await PayInstallmentAsync(stay, bookingId, 1);

        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/cancellation", stay.OwnerToken);

        // R$ 121,00: volta R$ 60,50; dos R$ 60,50 retidos, 90% (R$ 54,45) vão para a anfitriã.
        fixture.Stripe.Refunds.Should().Equal((intent, 6_050L));
        fixture.Stripe.Transfers.Should().Equal((_hostAccountId, 5_445L, $"ch_fake_{intent}"));
    }

    [Fact]
    public async Task Expiration_Should_CancelTheOpenCharge_When_TheOwnerDoesNotPayIn24Hours()
    {
        var stay = await ArrangeAsync();
        var bookingId = await RequestAndAcceptAsync(stay, nights: 2, checkIn: Today.AddDays(5));
        await SendAsync(HttpMethod.Put, PaymentRoute(bookingId), stay.OwnerToken, new { installmentCount = 1 });
        var intent = Data(await ReadAsync(await SendAsync(HttpMethod.Post, SessionRoute(bookingId, 1), stay.OwnerToken))).GetProperty("paymentIntentId").GetString()!;
        await fixture.ExecuteAsync($"UPDATE booking.bookings SET responded_at = now() - interval '25 hours' WHERE id = '{bookingId}'");

        await RunAsync<ExpireOverdueBookingsCommand, ExpireOverdueBookingsResponse>(new ExpireOverdueBookingsCommand());

        Status(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{bookingId}", stay.OwnerToken))).Should().Be("expired");
        fixture.Stripe.StateOf(intent).Should().Be(ChargeState.Canceled);
        Data(await ReadAsync(await SendAsync(HttpMethod.Get, PaymentRoute(bookingId), stay.OwnerToken))).GetProperty("status").GetString().Should().Be("cancelled");
    }

    // ----- parcela vencida -----

    [Fact]
    public async Task Maintenance_Should_ConfirmTheBooking_When_TheOwnerPaidButTheWebhookNeverArrived()
    {
        var stay = await ArrangeAsync();
        var bookingId = await RequestAndAcceptAsync(stay, nights: 2);
        await SendAsync(HttpMethod.Put, PaymentRoute(bookingId), stay.OwnerToken, new { installmentCount = 1 });
        var intent = Data(await ReadAsync(await SendAsync(HttpMethod.Post, SessionRoute(bookingId, 1), stay.OwnerToken))).GetProperty("paymentIntentId").GetString()!;

        // Pago com Pix no Stripe, mas nenhum webhook chega (API fora, destino errado, ngrok caído).
        fixture.Stripe.Pay(intent, PaymentMethod.Pix);

        await RunAsync<RunPaymentsMaintenanceCommand, RunPaymentsMaintenanceResponse>(new RunPaymentsMaintenanceCommand());

        Status(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{bookingId}", stay.OwnerToken))).Should().Be("confirmed");
        Data(await ReadAsync(await SendAsync(HttpMethod.Get, PaymentRoute(bookingId), stay.OwnerToken))).GetProperty("status").GetString().Should().Be("paid");
    }

    [Fact]
    public async Task Maintenance_Should_CancelTheBookingAndRefund_When_AnInstallmentIsMissedBeforeCheckIn()
    {
        var stay = await ArrangeAsync();
        var bookingId = await RequestAndAcceptAsync(stay, nights: 4, checkIn: Today.AddDays(40));
        await SendAsync(HttpMethod.Put, PaymentRoute(bookingId), stay.OwnerToken, new { installmentCount = 2 });
        var intent = await PayInstallmentAsync(stay, bookingId, 1);
        await fixture.ExecuteAsync("UPDATE payment.payment_installments SET due_at = now() - interval '25 hours' WHERE number = 2");

        await RunAsync<RunPaymentsMaintenanceCommand, RunPaymentsMaintenanceResponse>(new RunPaymentsMaintenanceCommand());

        var booking = Data(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{bookingId}", stay.OwnerToken)));
        booking.GetProperty("status").GetString().Should().Be("cancelled");
        booking.GetProperty("cancelledBy").GetString().Should().Be("platform");
        booking.GetProperty("refundPercentage").GetInt32().Should().Be(100);
        fixture.Stripe.Refunds.Should().Equal((intent, 12_100L));
    }

    [Fact]
    public async Task Maintenance_Should_MarkOverdueAndBlockNewStays_When_AnInstallmentIsMissedAfterCheckIn_UntilItIsPaid()
    {
        var stay = await ArrangeAsync();
        var bookingId = await RequestAndAcceptAsync(stay, nights: 4);
        await SendAsync(HttpMethod.Put, PaymentRoute(bookingId), stay.OwnerToken, new { installmentCount = 2 });
        await PayInstallmentAsync(stay, bookingId, 1);
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/check-in", stay.HostToken);
        await fixture.ExecuteAsync("UPDATE payment.payment_installments SET due_at = now() - interval '25 hours' WHERE number = 2");

        await RunAsync<RunPaymentsMaintenanceCommand, RunPaymentsMaintenanceResponse>(new RunPaymentsMaintenanceCommand());

        var payment = Data(await ReadAsync(await SendAsync(HttpMethod.Get, PaymentRoute(bookingId), stay.OwnerToken)));
        payment.GetProperty("installments")[1].GetProperty("status").GetString().Should().Be("overdue");
        Status(await ReadAsync(await SendAsync(HttpMethod.Get, $"{BookingsRoute}/{bookingId}", stay.OwnerToken))).Should().Be("in_progress");

        var blocked = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, Pedido(stay.ListingId, [stay.ThorId], Today.AddDays(10), 2));
        blocked.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(blocked)).Should().Be("BOOKING_OWNER_PAYMENT_OVERDUE");

        var issues = Data(await ReadAsync(await SendAsync(HttpMethod.Get, "/api/v1/payments/issues", await AdminTokenAsync())));
        issues.EnumerateArray().Select(i => i.GetProperty("issue").GetString()).Should().Contain("installment_overdue");

        await PayInstallmentAsync(stay, bookingId, 2);

        var allowed = await SendAsync(HttpMethod.Post, BookingsRoute, stay.OwnerToken, Pedido(stay.ListingId, [stay.ThorId], Today.AddDays(10), 2));
        allowed.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task OverduePayment_Should_ChargeTheFineAndInterest_AndTransferNinetyPercentOfWhatWasPaid()
    {
        var stay = await ArrangeAsync();

        // R$ 242,00 em 2x de R$ 121,00; a 1ª paga, estadia feita.
        var bookingId = await RequestAndAcceptAsync(stay, nights: 4);
        await SendAsync(HttpMethod.Put, PaymentRoute(bookingId), stay.OwnerToken, new { installmentCount = 2 });
        await PayInstallmentAsync(stay, bookingId, 1);
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/check-in", stay.HostToken);
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/check-out", stay.HostToken);

        // A 2ª venceu há 3 dias: multa de 2% (R$ 2,42) + juros de 1% ao mês por 3 dias (R$ 0,12).
        var dueAt = PaymentClock.EndOf(Today.AddDays(-3)).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
        await fixture.ExecuteAsync($"UPDATE payment.payment_installments SET due_at = '{dueAt}' WHERE number = 2");
        await RunAsync<RunPaymentsMaintenanceCommand, RunPaymentsMaintenanceResponse>(new RunPaymentsMaintenanceCommand());

        var overdue = Data(await ReadAsync(await SendAsync(HttpMethod.Get, PaymentRoute(bookingId), stay.OwnerToken))).GetProperty("installments")[1];
        overdue.GetProperty("status").GetString().Should().Be("overdue");
        overdue.GetProperty("amount").GetDecimal().Should().Be(121m);
        overdue.GetProperty("lateFee").GetDecimal().Should().Be(2.54m);
        overdue.GetProperty("amountDue").GetDecimal().Should().Be(123.54m);

        var notice = await WaitForEmailsAsync(e => e.Subject.Contains("parcela em atraso", StringComparison.Ordinal), 1);
        notice[0].TextBody.Should().Contain("123,54").And.Contain("multa de 2%");

        // O checkout cobra o valor com o acréscimo.
        var session = Data(await ReadAsync(await SendAsync(HttpMethod.Post, SessionRoute(bookingId, 2), stay.OwnerToken)));
        session.GetProperty("amount").GetDecimal().Should().Be(123.54m);
        session.GetProperty("lateFee").GetDecimal().Should().Be(2.54m);
        var intent = session.GetProperty("paymentIntentId").GetString()!;
        fixture.Stripe.AmountOf(intent).Should().Be(12_354);

        fixture.Stripe.Pay(intent);
        (await SendWebhookAsync("payment_intent.succeeded", intent, "payment_intent")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Repasse de 90% do que o tutor pagou (R$ 123,54 → R$ 111,18).
        fixture.Stripe.Transfers.Select(t => t.AmountCents).Should().Equal(10_890L, 11_118L);
        var hostView = Data(await ReadAsync(await SendAsync(HttpMethod.Get, PaymentRoute(bookingId), stay.HostToken)));
        hostView.GetProperty("status").GetString().Should().Be("paid");
        hostView.GetProperty("paidAmount").GetDecimal().Should().Be(244.54m);
        hostView.GetProperty("payout").GetProperty("transferred").GetDecimal().Should().Be(220.08m);
        hostView.GetProperty("installments")[1].GetProperty("lateFee").GetDecimal().Should().Be(2.54m);
        hostView.GetProperty("installments")[1].TryGetProperty("amountDue", out _).Should().BeFalse();
    }

    // ----- contestação -----

    [Fact]
    public async Task Dispute_Should_HoldThenReverseTheHostTransfer_When_TheOwnerWinsTheChargeback()
    {
        var stay = await ArrangeAsync();
        var bookingId = await RequestAndAcceptAsync(stay, nights: 2);
        await SendAsync(HttpMethod.Put, PaymentRoute(bookingId), stay.OwnerToken, new { installmentCount = 1 });
        var intent = await PayInstallmentAsync(stay, bookingId, 1);
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/check-in", stay.HostToken);
        await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/check-out", stay.HostToken);
        fixture.Stripe.Transfers.Should().ContainSingle();

        var disputeId = fixture.Stripe.OpenDispute(intent);
        (await SendWebhookAsync("charge.dispute.created", disputeId, "dispute")).StatusCode.Should().Be(HttpStatusCode.OK);

        var admin = await AdminTokenAsync();
        var issues = Data(await ReadAsync(await SendAsync(HttpMethod.Get, "/api/v1/payments/issues", admin)));
        issues.EnumerateArray().Select(i => i.GetProperty("issue").GetString()).Should().Contain("dispute_open");

        fixture.Stripe.CloseDispute(intent, DisputeStatus.Lost);
        (await SendWebhookAsync("charge.dispute.closed", disputeId, "dispute")).StatusCode.Should().Be(HttpStatusCode.OK);

        fixture.Stripe.Reversals.Should().Equal(("tr_fake_1", 10_890L));
        var adminView = Data(await ReadAsync(await SendAsync(HttpMethod.Get, PaymentRoute(bookingId), admin)));
        var installment = adminView.GetProperty("installments")[0];
        installment.GetProperty("payoutStatus").GetString().Should().Be("reversed");
        installment.GetProperty("disputeStatus").GetString().Should().Be("lost");
        installment.GetProperty("stripe").GetProperty("disputeId").GetString().Should().Be(disputeId);
        (await WaitForEmailsAsync(e => e.Subject.Contains("repasse estornado", StringComparison.Ordinal), 1)).Single().ToAddress.Should().Be("cida@exemplo.com");
        (await WaitForEmailsAsync(e => e.ToAddress == PaymentsApiFixture.AdminEmail && e.Subject.Contains("dispute_lost", StringComparison.Ordinal), 1)).Should().ContainSingle();
    }

    // ----- webhook -----

    [Fact]
    public async Task Webhook_Should_Return400_When_TheSignatureIsWrong()
    {
        var payload = StripeWebhookSigner.Event("payment_intent.succeeded", "pi_any", "payment_intent");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/stripe/webhooks")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", StripeWebhookSigner.Sign(payload, "whsec_wrong"));

        var response = await _client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Webhook_Should_IgnoreAChargeThatIsNotFromAnyInstallment()
    {
        var response = await SendWebhookAsync("payment_intent.succeeded", "pi_from_somewhere_else", "payment_intent");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ----- helpers -----

    private static string PaymentRoute(Guid bookingId) => $"{BookingsRoute}/{bookingId}/payment";

    private static string SessionRoute(Guid bookingId, int number) => $"{PaymentRoute(bookingId)}/installments/{number}/payment-session";

    /// <summary>Abre o checkout da parcela, o tutor paga no cartão e o Stripe avisa pelo webhook.</summary>
    private async Task<string> PayInstallmentAsync(Stay stay, Guid bookingId, int number)
    {
        var session = await SendAsync(HttpMethod.Post, SessionRoute(bookingId, number), stay.OwnerToken);
        session.StatusCode.Should().Be(HttpStatusCode.OK);
        var intent = Data(await ReadAsync(session)).GetProperty("paymentIntentId").GetString()!;

        fixture.Stripe.Pay(intent);
        (await SendWebhookAsync("payment_intent.succeeded", intent, "payment_intent")).StatusCode.Should().Be(HttpStatusCode.OK);

        return intent;
    }

    private Task<HttpResponseMessage> SendWebhookAsync(string type, string objectId, string objectType, string? eventId = null)
    {
        var payload = StripeWebhookSigner.Event(type, objectId, objectType, eventId);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/stripe/webhooks")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", StripeWebhookSigner.Sign(payload));

        return _client.SendAsync(request, Ct);
    }

    private async Task<TResponse> RunAsync<TCommand, TResponse>(TCommand command)
        where TCommand : ICommand<TResponse>
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand, TResponse>>().HandleAsync(command, Ct);
        result.IsSuccess.Should().BeTrue();

        return result.Value!;
    }

    private async Task<List<PetHost.Shared.Infrastructure.Email.EmailMessage>> WaitForEmailsAsync(Func<PetHost.Shared.Infrastructure.Email.EmailMessage, bool> match, int count)
    {
        for (var attempt = 0; attempt < 100 && fixture.SentEmails.Count(match) < count; attempt++)
            await Task.Delay(100, Ct);

        return [.. fixture.SentEmails.Where(match)];
    }

    private sealed record Stay(string OwnerToken, string HostToken, Guid ListingId, Guid PipocaId, Guid ThorId);

    /// <summary>A Camila (com a Pipoca e o Thor) e o cantinho da Dona Cida, com conta conectada no Stripe.</summary>
    private async Task<Stay> ArrangeAsync()
    {
        var owner = await RegisterOwnerAsync("camila@exemplo.com", "529.982.247-25", "Camila Souza");
        var pipoca = await RegisterPetAsync(owner, "Pipoca");
        var thor = await RegisterPetAsync(owner, "Thor", size: "small");

        var host = await RegisterHostAsync();
        var listing = await CreateListingAsync(host);

        return new Stay(owner, host, listing, pipoca, thor);
    }

    /// <summary>A Camila pede para a Pipoca e a Dona Cida aceita.</summary>
    private async Task<Guid> RequestAndAcceptAsync(Stay stay, int nights, DateOnly? checkIn = null)
    {
        var bookingId = await CreateBookingAsync(stay.OwnerToken, stay.ListingId, stay.PipocaId, checkIn ?? Today, nights);

        var accepted = await SendAsync(HttpMethod.Post, $"{BookingsRoute}/{bookingId}/acceptance", stay.HostToken);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        Status(await ReadAsync(accepted)).Should().Be("accepted");

        return bookingId;
    }

    private async Task<string> RegisterOwnerAsync(string email, string cpf, string fullName)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/owners/register", new
        {
            fullName,
            email,
            password = Password,
            phone = "(44) 99999-0000",
            birthDate = "1990-05-10",
            cpf,
            address = new { zipCode = "87020-000", street = "Rua Joubert de Carvalho", number = "500", neighborhood = "Zona 7", city = "Maringá", state = "PR" },
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return Data(await ReadAsync(response)).GetProperty("accessToken").GetString()!;
    }

    /// <summary>A Dona Cida: conta de anfitriã, verificada e com os repasses liberados pelo Stripe.</summary>
    private async Task<string> RegisterHostAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/users/register", new
        {
            fullName = "Dona Cida",
            email = "cida@exemplo.com",
            password = Password,
            phone = "(44) 99999-0000",
            birthDate = "1965-04-21",
            address = new { zipCode = "87020-000", street = "Rua das Flores", number = "120", neighborhood = "Zona 7", city = "Maringá", state = "PR" },
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var data = Data(await ReadAsync(response));
        var userId = data.GetProperty("user").GetProperty("id").GetGuid();

        var host = Host.CreateIndividual(userId, Cpf.Create("12345678909").Value!, Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow);
        _hostAccountId = $"acct_test_{host.Id.Value:N}";
        host.LinkStripeAccount(_hostAccountId, DateTimeOffset.UtcNow);
        host.ApplyStripeSnapshot(new StripeAccountSnapshot(true, true, true, null), DateTimeOffset.UtcNow);

        await using var scope = fixture.Services.CreateAsyncScope();
        var hosts = scope.ServiceProvider.GetRequiredService<HostsDbContext>();
        hosts.Hosts.Add(host);
        await hosts.SaveChangesAsync(Ct);

        return data.GetProperty("accessToken").GetString()!;
    }

    private async Task<string> AdminTokenAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/sessions/login",
            new { email = PaymentsApiFixture.AdminEmail, password = PaymentsApiFixture.AdminPassword, role = "admin" },
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return Data(await ReadAsync(response)).GetProperty("accessToken").GetString()!;
    }

    private async Task<Guid> RegisterPetAsync(string token, string name, string size = "medium")
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/pets", token, new
        {
            species = "dog",
            name,
            breed = "SRD",
            size,
            birthDate = "2022-03-15",
            sex = "female",
            isNeutered = true,
            isVaccinated = true,
            goodWithDogs = true,
            goodWithCats = false,
            goodWithKids = true,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return Data(await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateListingAsync(string token)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/listings", token, new
        {
            title = "Casa com quintal",
            description = "Casa térrea, quintal gramado e muito carinho.",
            homeType = "house",
            hasYard = true,
            hasDogs = true,
            hasCats = false,
            hasKids = false,
            acceptedSpecies = DogsAndCats,
            acceptsSmall = true,
            acceptsMedium = true,
            acceptsLarge = false,
            capacity = 3,
            nightlyRate = 60.50m,
            availableWeekdays = EveryDay,
            address = new { zipCode = "87020-000", street = "Rua das Flores", number = "120", neighborhood = "Zona 7", city = "Maringá", state = "PR" },
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return Data(await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateBookingAsync(string token, Guid listingId, Guid petId, DateOnly checkIn, int nights)
    {
        var response = await SendAsync(HttpMethod.Post, BookingsRoute, token, Pedido(listingId, [petId], checkIn, nights));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return Data(await ReadAsync(response)).GetProperty("id").GetGuid();
    }

    private static object Pedido(Guid listingId, Guid[] petIds, DateOnly checkIn, int nights) =>
        new { listingId, petIds, checkIn = Iso(checkIn), checkOut = Iso(checkIn.AddDays(nights)) };

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        return _client.SendAsync(request, Ct);
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);

    private static JsonElement Data(JsonElement body) => body.GetProperty("data");

    private static string? Status(JsonElement body) => Data(body).GetProperty("status").GetString();

    private static string? Code(JsonElement body) => body.GetProperty("error").GetProperty("code").GetString();

    private static List<string?> Fields(JsonElement body) =>
        body.GetProperty("error").TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
            ? [.. details.EnumerateArray().Select(d => d.GetProperty("field").GetString())]
            : [];
}
