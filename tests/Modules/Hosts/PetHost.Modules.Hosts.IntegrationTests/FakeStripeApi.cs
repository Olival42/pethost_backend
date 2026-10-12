using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using PetHost.Shared.Infrastructure.Stripe;
using Stripe;

namespace PetHost.Modules.Hosts.IntegrationTests;

/// <summary>
/// A API do Stripe de mentira, no lugar da rede: o SDK de verdade (e o nosso adaptador) fala com
/// este handler. Guarda o estado de cada conta conectada e os pedidos recebidos, para o teste
/// conferir o que foi mandado ao Stripe.
/// </summary>
public sealed class FakeStripeApi : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, AccountState> _accounts = new(StringComparer.Ordinal);
    private int _sequence;

    /// <summary>Pedidos recebidos, na ordem: método, caminho, corpo (form) e chave de idempotência.</summary>
    public ConcurrentQueue<StripeRequest> Requests { get; } = new();

    /// <summary>Simula o Stripe fora do ar: toda chamada responde 500.</summary>
    public bool Unavailable { get; set; }

    public IEnumerable<StripeRequest> AccountCreations =>
        Requests.Where(r => r.Method == "POST" && r.Path == "/v2/core/accounts");

    /// <summary>Muda o que o Stripe diz da conta (ex.: liberou os repasses).</summary>
    public void SetAccount(string accountId, bool detailsSubmitted, bool payoutsEnabled, string? disabledReason) =>
        _accounts[accountId] = new AccountState(detailsSubmitted, payoutsEnabled, payoutsEnabled, disabledReason);

    public void Reset()
    {
        _accounts.Clear();
        Requests.Clear();
        Unavailable = false;
    }

    /// <summary>Um provedor de cliente que manda o SDK para esta API.</summary>
    public IStripeClientProvider ClientProvider() => new Provider(this);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var idempotencyKey = request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.FirstOrDefault() : null;
        var isJson = request.Content?.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;
        Requests.Enqueue(new StripeRequest(request.Method.Method, path, isJson ? body : Uri.UnescapeDataString(body.Replace('+', ' ')), idempotencyKey));

        if (Unavailable)
            return Json(HttpStatusCode.InternalServerError, """{ "error": { "type": "api_error", "message": "Stripe de teste fora do ar." } }""");

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return (request.Method.Method, segments) switch
        {
            ("POST", ["v2", "core", "accounts"]) => V2Account(CreateAccount()),
            ("GET", ["v1", "accounts", var id]) => Account(id),
            ("POST", ["v1", "accounts", var id, "persons"]) =>
                Json(HttpStatusCode.OK, $$"""{ "id": "person_test", "object": "person", "account": "{{id}}" }"""),
            ("POST", ["v1", "account_sessions"]) =>
                Json(HttpStatusCode.OK, $$"""{ "object": "account_session", "account": "acct", "client_secret": "accs_secret_test", "expires_at": {{now + 1800}}, "livemode": false }"""),
            ("POST", ["v1", "account_links"]) =>
                Json(HttpStatusCode.OK, $$"""{ "object": "account_link", "url": "https://connect.stripe.com/setup/e/acct_test/link", "created": {{now}}, "expires_at": {{now + 300}} }"""),
            _ => Json(HttpStatusCode.NotFound, """{ "error": { "type": "invalid_request_error", "message": "Rota desconhecida no Stripe de teste." } }"""),
        };
    }

    private string CreateAccount()
    {
        var id = string.Create(CultureInfo.InvariantCulture, $"acct_test{Interlocked.Increment(ref _sequence):D4}");
        _accounts[id] = new AccountState(false, false, false, "requirements.past_due");
        return id;
    }

    /// <summary>Resposta da criação pela Accounts v2 (só o que o adaptador lê: o id).</summary>
    private static HttpResponseMessage V2Account(string id) =>
        Json(HttpStatusCode.OK, $$"""{ "id": "{{id}}", "object": "v2.core.account", "dashboard": "express", "livemode": false, "created": "2026-10-12T12:00:00.000Z" }""");

    private HttpResponseMessage Account(string id)
    {
        // Conta que o teste não configurou (ex.: a do seed) ainda está no meio do cadastro.
        var state = _accounts.GetOrAdd(id, _ => new AccountState(false, false, false, "requirements.past_due"));
        var reason = state.DisabledReason is null ? "null" : $"\"{state.DisabledReason}\"";

        return Json(HttpStatusCode.OK, $$"""
            {
              "id": "{{id}}", "object": "account", "country": "BR", "type": "express",
              "details_submitted": {{Bool(state.DetailsSubmitted)}},
              "charges_enabled": {{Bool(state.ChargesEnabled)}},
              "payouts_enabled": {{Bool(state.PayoutsEnabled)}},
              "requirements": { "disabled_reason": {{reason}}, "currently_due": [] }
            }
            """);
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed record AccountState(bool DetailsSubmitted, bool ChargesEnabled, bool PayoutsEnabled, string? DisabledReason);

    private sealed class Provider(FakeStripeApi api) : IStripeClientProvider
    {
        public IStripeClient? Client { get; } = new StripeClient(
            "sk_test_pethost_fake",
            httpClient: new SystemNetHttpClient(new HttpClient(api), maxNetworkRetries: 0));

        public string PublishableKey => "pk_test_pethost_fake";
    }
}

/// <summary>
/// Um pedido que chegou no Stripe de teste. <c>Body</c>: JSON nos endpoints v2; form decodificado
/// (<c>type=account_onboarding</c>...) nos v1.
/// </summary>
public sealed record StripeRequest(string Method, string Path, string Body, string? IdempotencyKey);
