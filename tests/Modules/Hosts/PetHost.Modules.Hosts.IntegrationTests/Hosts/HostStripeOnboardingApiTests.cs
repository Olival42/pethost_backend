using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Hosts.Application.Hosts.RunPayoutsMaintenance;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Modules.Hosts.Infrastructure.Persistence;
using PetHost.Shared.Contracts.Hosts;
using PetHost.Shared.Infrastructure.Email;
using PetHost.Shared.Kernel.Messaging;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Hosts.IntegrationTests.Hosts;

/// <summary>
/// Cadastro de recebimento (Stripe Connect) pela API, com o SDK de verdade falando com o Stripe de
/// mentira (<see cref="FakeStripeApi"/>): só depois da aprovação do admin, conta conectada criada
/// uma vez e já preenchida, webhook assinado e cantinho liberado só quando o Stripe aprova.
/// </summary>
[Collection(HostsApiCollection.Name)]
public sealed class HostStripeOnboardingApiTests(HostsApiFixture fixture) : IAsyncLifetime
{
    private const string Route = "/api/v1/hosts/me/stripe-account";
    private const string WebhookRoute = "/api/v1/stripe/webhooks";
    private const string HostCpf = "52998224725";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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

    // ----- só depois da aprovação -----

    [Fact]
    public async Task Onboarding_Should_Return404_When_TheAdminHasNotApprovedTheHostYet()
    {
        // Arrange: conta de anfitrião sem perfil = verificação ainda não aprovada.
        var (token, _) = await RegisterHostAccountAsync("pendente@exemplo.com");

        // Act
        var session = await SendAsync(HttpMethod.Post, $"{Route}/onboarding-session", token);
        var status = await SendAsync(HttpMethod.Get, Route, token);

        // Assert
        session.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(session).Should().Be("HOST_NOT_FOUND");
        status.StatusCode.Should().Be(HttpStatusCode.NotFound);
        fixture.Stripe.AccountCreations.Should().BeEmpty("não cria conta conectada para quem pode ser recusado");
    }

    [Fact]
    public async Task Onboarding_Should_Return403_When_CalledByAnOwner()
    {
        // Arrange
        var response = await _client.PostAsJsonAsync("/api/v1/owners/register", new
        {
            fullName = "Camila Rocha",
            email = "camila@exemplo.com",
            password = "Nova@Senha123",
            phone = "(44) 98888-0000",
            birthDate = "1990-05-10",
            cpf = "111.444.777-35",
            address = new { zipCode = "87033-000", street = "Rua B", number = "2", neighborhood = "Centro", city = "Maringá", state = "PR" },
        }, Ct);
        var token = Data(response).GetProperty("accessToken").GetString()!;

        // Act
        var session = await SendAsync(HttpMethod.Post, $"{Route}/onboarding-session", token);

        // Assert
        session.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ----- conta conectada e sessão -----

    [Fact]
    public async Task Onboarding_Should_CreateOnePrefilledAccountAndOpenSessions_When_HostIsApproved()
    {
        // Arrange
        var token = await ApprovedHostAsync("cida@exemplo.com");

        // Act
        var first = await SendAsync(HttpMethod.Post, $"{Route}/onboarding-session", token);
        var second = await SendAsync(HttpMethod.Post, $"{Route}/onboarding-session", token);
        var status = await SendAsync(HttpMethod.Get, Route, token);

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Headers.CacheControl!.NoStore.Should().BeTrue();
        var data = Data(first);
        data.GetProperty("clientSecret").GetString().Should().Be("accs_secret_test");
        data.GetProperty("publishableKey").GetString().Should().Be("pk_test_pethost_fake");
        data.GetProperty("expiresAt").GetInt64().Should().BeGreaterThan(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        data.GetProperty("account").GetProperty("status").GetString().Should().Be("in_progress");
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var creation = fixture.Stripe.AccountCreations.Should().ContainSingle("a segunda sessão reaproveita a conta").Subject;
        // Accounts v2 (POST /v2/core/accounts): o corpo é JSON.
        var sent = JsonDocument.Parse(creation.Body).RootElement;
        sent.GetProperty("dashboard").GetString().Should().Be("express");
        sent.GetProperty("defaults").GetProperty("responsibilities").GetProperty("losses_collector").GetString().Should().Be("application");
        sent.GetProperty("configuration").GetProperty("recipient").GetProperty("capabilities")
            .GetProperty("stripe_balance").GetProperty("stripe_transfers").GetProperty("requested").GetBoolean().Should().BeTrue();
        sent.GetProperty("configuration").GetProperty("merchant").GetProperty("capabilities")
            .GetProperty("card_payments").GetProperty("requested").GetBoolean().Should().BeTrue("no Brasil o Stripe exige card_payments junto com stripe_transfers");
        var identity = sent.GetProperty("identity");
        identity.GetProperty("country").GetString().Should().Be("BR");
        identity.GetProperty("entity_type").GetString().Should().Be("individual");
        var individual = identity.GetProperty("individual");
        individual.GetProperty("given_name").GetString().Should().Be("Maria");
        individual.GetProperty("surname").GetString().Should().Be("Souza");
        individual.GetProperty("phone").GetString().Should().Be("+5544999990000");
        individual.GetProperty("date_of_birth").GetProperty("year").GetInt32().Should().Be(1970);
        individual.GetProperty("id_numbers")[0].GetProperty("type").GetString().Should().Be("br_cpf");
        individual.GetProperty("id_numbers")[0].GetProperty("value").GetString().Should().Be(HostCpf);
        individual.GetProperty("address").GetProperty("line1").GetString().Should().Be("Rua A, 1");
        individual.GetProperty("address").GetProperty("postal_code").GetString().Should().Be("87033-000");
        creation.IdempotencyKey.Should().StartWith("pethost-host-");

        Data(status).GetProperty("status").GetString().Should().Be("in_progress");
        Data(status).GetProperty("listingsVisible").GetBoolean().Should().BeFalse();
        (await HostAsync("cida@exemplo.com")).StripeAccountId.Should().StartWith("acct_test");
    }

    [Fact]
    public async Task OnboardingLink_Should_ReturnTheHostedPage_When_HostIsApproved()
    {
        // Arrange
        var token = await ApprovedHostAsync("cida@exemplo.com");

        // Act
        var response = await SendAsync(HttpMethod.Post, $"{Route}/onboarding-link", token);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(response).GetProperty("url").GetString().Should().StartWith("https://connect.stripe.com/");
        var link = fixture.Stripe.Requests.Should().ContainSingle(r => r.Path == "/v1/account_links").Subject;
        link.Body.Should().Contain("type=account_onboarding").And.Contain("return_url=http://localhost:3000/");
    }

    [Fact]
    public async Task Onboarding_Should_Return503AndKeepNothing_When_StripeIsDown()
    {
        // Arrange
        var token = await ApprovedHostAsync("cida@exemplo.com");
        fixture.Stripe.Unavailable = true;

        // Act
        var response = await SendAsync(HttpMethod.Post, $"{Route}/onboarding-session", token);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        Code(response).Should().Be("HOST_STRIPE_UNAVAILABLE");
        (await HostAsync("cida@exemplo.com")).StripeAccountId.Should().BeNull();
    }

    [Fact]
    public async Task Synchronization_Should_Return404_When_OnboardingWasNotStarted()
    {
        // Arrange
        var token = await ApprovedHostAsync("cida@exemplo.com");

        // Act
        var response = await SendAsync(HttpMethod.Post, $"{Route}/synchronization", token);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(response).Should().Be("HOST_STRIPE_ACCOUNT_NOT_FOUND");
    }

    // ----- Stripe aprova: o anfitrião fica disponível -----

    [Fact]
    public async Task Webhook_Should_MakeTheHostListable_When_StripeEnablesPayouts()
    {
        // Arrange: o anfitrião de demonstração já tem conta conectada, ainda em cadastro.
        var token = await DemoHostTokenAsync();
        var hostId = Data(await SendAsync(HttpMethod.Get, "/api/v1/hosts/me", token)).GetProperty("id").GetGuid();
        (await UnavailableHostIdsAsync()).Should().Contain(hostId, "sem repasses liberados o cantinho não aparece");

        fixture.Stripe.SetAccount(HostsApiFixture.DemoHostStripeAccount, detailsSubmitted: true, payoutsEnabled: true, disabledReason: null);

        // Act
        var webhook = await PostWebhookAsync(StripeWebhookSigner.AccountUpdated(HostsApiFixture.DemoHostStripeAccount));
        var status = await SendAsync(HttpMethod.Get, Route, token);

        // Assert
        webhook.StatusCode.Should().Be(HttpStatusCode.OK);
        Read(webhook).GetProperty("success").GetBoolean().Should().BeTrue();
        var data = Data(status);
        data.GetProperty("status").GetString().Should().Be("enabled");
        data.GetProperty("payoutsEnabled").GetBoolean().Should().BeTrue();
        data.GetProperty("listingsVisible").GetBoolean().Should().BeTrue();
        data.GetProperty("onboardedAt").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        (await UnavailableHostIdsAsync()).Should().NotContain(hostId);
        Data(await SendAsync(HttpMethod.Get, "/api/v1/hosts/me", token)).GetProperty("stripeStatus").GetString().Should().Be("enabled");
    }

    [Fact]
    public async Task Synchronization_Should_ReadTheAccountRightAway_When_HostComesBackFromStripe()
    {
        // Arrange
        var token = await DemoHostTokenAsync();
        fixture.Stripe.SetAccount(HostsApiFixture.DemoHostStripeAccount, detailsSubmitted: true, payoutsEnabled: false, disabledReason: "requirements.pending_verification");

        // Act
        var response = await SendAsync(HttpMethod.Post, $"{Route}/synchronization", token);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(response).GetProperty("status").GetString().Should().Be("under_review");
        Data(response).GetProperty("listingsVisible").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Webhook_Should_HideTheListingsAgain_When_StripeBlocksPayouts()
    {
        // Arrange
        var token = await DemoHostTokenAsync();
        fixture.Stripe.SetAccount(HostsApiFixture.DemoHostStripeAccount, true, true, null);
        await PostWebhookAsync(StripeWebhookSigner.AccountUpdated(HostsApiFixture.DemoHostStripeAccount, "evt_1"));
        fixture.Stripe.SetAccount(HostsApiFixture.DemoHostStripeAccount, true, false, "requirements.past_due");

        // Act
        var webhook = await PostWebhookAsync(StripeWebhookSigner.AccountUpdated(HostsApiFixture.DemoHostStripeAccount, "evt_2"));

        // Assert
        webhook.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = Data(await SendAsync(HttpMethod.Get, Route, token));
        data.GetProperty("status").GetString().Should().Be("restricted");
        data.GetProperty("disabledReason").GetString().Should().Be("requirements.past_due");
        data.GetProperty("listingsVisible").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Webhook_Should_Return400AndChangeNothing_When_TheSignatureIsWrong()
    {
        // Arrange
        fixture.Stripe.SetAccount(HostsApiFixture.DemoHostStripeAccount, true, true, null);
        var payload = StripeWebhookSigner.AccountUpdated(HostsApiFixture.DemoHostStripeAccount);

        // Act
        var response = await PostWebhookAsync(payload, StripeWebhookSigner.Sign(payload, "whsec_forjado"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Code(response).Should().Be("VALIDATION_ERROR");
        Read(response).GetProperty("error").GetProperty("details")[0].GetProperty("field").GetString().Should().Be("stripeSignature");
        (await HostAsync(HostsApiFixture.DemoHostEmail)).StripePayoutsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Webhook_Should_AcceptAndIgnore_When_TheAccountIsNotFromAnyHost()
    {
        // Act
        var response = await PostWebhookAsync(StripeWebhookSigner.AccountUpdated("acct_1DeNinguem"));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Webhook_Should_Return500SoStripeRetries_When_StripeIsDownOnTheReread()
    {
        // Arrange
        fixture.Stripe.Unavailable = true;

        // Act
        var response = await PostWebhookAsync(StripeWebhookSigner.AccountUpdated(HostsApiFixture.DemoHostStripeAccount));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        Code(response).Should().Be("UNEXPECTED_ERROR");
    }

    // ----- quando o cadastro não anda -----

    [Fact]
    public async Task Maintenance_Should_EnableTheHost_When_TheWebhookNeverArrived()
    {
        // Arrange: o Stripe aprovou a conta do anfitrião de demonstração, mas nenhum webhook chegou.
        var token = await DemoHostTokenAsync();
        fixture.Stripe.SetAccount(HostsApiFixture.DemoHostStripeAccount, detailsSubmitted: true, payoutsEnabled: true, disabledReason: null);

        // Act
        var result = await RunMaintenanceAsync();

        // Assert
        result.Synced.Should().BeGreaterThanOrEqualTo(1);
        Data(await SendAsync(HttpMethod.Get, Route, token)).GetProperty("listingsVisible").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Webhook_Should_MarkRejectedAndAlertHostAndAdmin_When_StripeRejectsTheAccount()
    {
        // Arrange
        var token = await DemoHostTokenAsync();
        fixture.Stripe.SetAccount(HostsApiFixture.DemoHostStripeAccount, detailsSubmitted: true, payoutsEnabled: false, disabledReason: "rejected.fraud");

        // Act
        var webhook = await PostWebhookAsync(StripeWebhookSigner.AccountUpdated(HostsApiFixture.DemoHostStripeAccount));
        var status = Data(await SendAsync(HttpMethod.Get, Route, token));
        var rejected = await SendAsync(HttpMethod.Get, "/api/v1/hosts?stripeStatus=rejected", await AdminTokenAsync());

        // Assert
        webhook.StatusCode.Should().Be(HttpStatusCode.OK);
        status.GetProperty("status").GetString().Should().Be("rejected");
        status.GetProperty("disabledReason").GetString().Should().Be("rejected.fraud");
        Data(rejected).GetArrayLength().Should().Be(1);

        var emails = await WaitForEmailsAsync(2);
        emails.Should().Contain(e => e.ToAddress == HostsApiFixture.DemoHostEmail && e.Subject.Contains("não foi possível aprovar"));
        emails.Should().Contain(e => e.ToAddress == HostsApiFixture.AdminEmail && e.TextBody.Contains("rejected.fraud"));
    }

    [Fact]
    public async Task Maintenance_Should_RemindByEmail_When_TheHostStoppedHalfway()
    {
        // Arrange: abriu o cadastro há 4 dias e parou no meio.
        await DemoHostTokenAsync();
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostsDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE host.hosts SET stripe_account_created_at = now() - interval '4 days', verified_at = now() - interval '4 days'", Ct);
        }

        // Act
        var result = await RunMaintenanceAsync();
        var again = await RunMaintenanceAsync();

        // Assert
        result.Reminded.Should().Be(1);
        again.Reminded.Should().Be(0, "o próximo lembrete é só no dia 7");
        (await HostAsync(HostsApiFixture.DemoHostEmail)).StripeRemindersSent.Should().Be(1);
        var emails = await WaitForEmailsAsync(1);
        emails.Should().Contain(e => e.ToAddress == HostsApiFixture.DemoHostEmail && e.TextBody.Contains("continue de onde parou"));
    }

    [Fact]
    public async Task AdminList_Should_FilterByStripeStatus_When_Asked()
    {
        // Arrange: o anfitrião de demonstração está no meio do cadastro.
        var admin = await AdminTokenAsync();

        // Act
        var inProgress = await SendAsync(HttpMethod.Get, "/api/v1/hosts?stripeStatus=in_progress", admin);
        var enabled = await SendAsync(HttpMethod.Get, "/api/v1/hosts?stripeStatus=enabled", admin);
        var invalid = await SendAsync(HttpMethod.Get, "/api/v1/hosts?stripeStatus=aprovado", admin);

        // Assert
        Data(inProgress).GetArrayLength().Should().Be(1);
        Data(inProgress)[0].GetProperty("stripeStatus").GetString().Should().Be("in_progress");
        Data(enabled).GetArrayLength().Should().Be(0);
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Read(invalid).GetProperty("error").GetProperty("details")[0].GetProperty("field").GetString().Should().Be("stripeStatus");
    }

    [Fact]
    public async Task Reset_Should_BlockTheRejectedHostUntilTheAdminFreesANewAccount_When_StripeRejects()
    {
        // Arrange: o Stripe recusou a conta do anfitrião de demonstração.
        var token = await DemoHostTokenAsync();
        var hostId = Data(await SendAsync(HttpMethod.Get, "/api/v1/hosts/me", token)).GetProperty("id").GetGuid();
        fixture.Stripe.SetAccount(HostsApiFixture.DemoHostStripeAccount, true, false, "rejected.other");
        await PostWebhookAsync(StripeWebhookSigner.AccountUpdated(HostsApiFixture.DemoHostStripeAccount));
        var admin = await AdminTokenAsync();

        // Act
        var blocked = await SendAsync(HttpMethod.Post, $"{Route}/onboarding-link", token);
        var reset = await SendJsonAsync(HttpMethod.Post, $"/api/v1/hosts/{hostId}/stripe-account-reset", admin, new { reason = "Recusa por dado digitado errado" });
        var resetAgain = await SendJsonAsync(HttpMethod.Post, $"/api/v1/hosts/{hostId}/stripe-account-reset", admin, new { reason = "de novo" });
        var reopened = await SendAsync(HttpMethod.Post, $"{Route}/onboarding-link", token);

        // Assert
        blocked.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(blocked).Should().Be("HOST_STRIPE_ACCOUNT_REJECTED");
        reset.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(reset).GetProperty("stripeStatus").GetString().Should().Be("not_started");
        resetAgain.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(resetAgain).Should().Be("HOST_STRIPE_ACCOUNT_NOT_REJECTED");
        reopened.StatusCode.Should().Be(HttpStatusCode.OK);

        var host = await HostAsync(HostsApiFixture.DemoHostEmail);
        host.StripeRejectedAccountId.Should().Be(HostsApiFixture.DemoHostStripeAccount);
        host.StripeAccountId.Should().StartWith("acct_test", "uma conta nova foi criada no Stripe");
        fixture.Stripe.AccountCreations.Should().ContainSingle()
            .Which.IdempotencyKey.Should().EndWith("-1", "a chave muda depois do reset, senão o Stripe devolveria a conta recusada");
    }

    // ----- helpers -----

    private Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string route, string token, object body)
    {
        var request = new HttpRequestMessage(method, route) { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return _client.SendAsync(request, Ct);
    }

    private async Task<RunPayoutsMaintenanceResponse> RunMaintenanceAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RunPayoutsMaintenanceCommand, RunPayoutsMaintenanceResponse>>();
        var result = await handler.HandleAsync(new RunPayoutsMaintenanceCommand(), Ct);
        return result.Value!;
    }

    private async Task<string> AdminTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/sessions/login",
            new { email = HostsApiFixture.AdminEmail, password = HostsApiFixture.AdminPassword, role = "admin" }, Ct);
        return Data(response).GetProperty("accessToken").GetString()!;
    }

    /// <summary>O despachante de e-mail roda em segundo plano: espera até chegarem <paramref name="count"/> e-mails.</summary>
    private async Task<List<EmailMessage>> WaitForEmailsAsync(int count)
    {
        for (var attempt = 0; attempt < 100 && fixture.SentEmails.Count < count; attempt++)
            await Task.Delay(100, Ct);

        return [.. fixture.SentEmails];
    }

    /// <summary>Conta de anfitrião com o perfil gravado direto, como se o admin já tivesse aprovado.</summary>
    private async Task<string> ApprovedHostAsync(string email)
    {
        var (token, userId) = await RegisterHostAccountAsync(email);

        await using var scope = fixture.Services.CreateAsyncScope();
        var hosts = scope.ServiceProvider.GetRequiredService<HostsDbContext>();
        hosts.Hosts.Add(Host.CreateIndividual(userId, Cpf.Create(HostCpf).Value!, Guid.CreateVersion7(), Guid.CreateVersion7(), DateTimeOffset.UtcNow));
        await hosts.SaveChangesAsync(Ct);

        return token;
    }

    private async Task<(string Token, Guid UserId)> RegisterHostAccountAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/users/register", new
        {
            fullName = "Maria Souza",
            email,
            password = "Nova@Senha123",
            phone = "(44) 99999-0000",
            birthDate = "1970-01-01",
            address = new { zipCode = "87033-000", street = "Rua A", number = "1", neighborhood = "Centro", city = "Maringá", state = "PR" },
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var data = Data(response);
        return (data.GetProperty("accessToken").GetString()!, data.GetProperty("user").GetProperty("id").GetGuid());
    }

    private async Task<string> DemoHostTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/sessions/login",
            new { email = HostsApiFixture.DemoHostEmail, password = HostsApiFixture.DemoHostPassword, role = "host" }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return Data(response).GetProperty("accessToken").GetString()!;
    }

    private async Task<Host> HostAsync(string email)
    {
        var (_, userId) = await LoginIdAsync(email);

        await using var scope = fixture.Services.CreateAsyncScope();
        var hosts = scope.ServiceProvider.GetRequiredService<HostsDbContext>();
        return await hosts.Hosts.AsNoTracking().SingleAsync(h => h.UserId == userId, Ct);
    }

    private async Task<(string Token, Guid UserId)> LoginIdAsync(string email)
    {
        var password = email == HostsApiFixture.DemoHostEmail ? HostsApiFixture.DemoHostPassword : "Nova@Senha123";
        var response = await _client.PostAsJsonAsync("/api/v1/auth/sessions/login", new { email, password, role = "host" }, Ct);
        var data = Data(response);
        return (data.GetProperty("accessToken").GetString()!, data.GetProperty("user").GetProperty("id").GetGuid());
    }

    private async Task<IReadOnlyList<Guid>> UnavailableHostIdsAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IHostDirectory>().GetUnavailableIdsAsync(Ct);
    }

    private Task<HttpResponseMessage> PostWebhookAsync(string payload, string? signature = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, WebhookRoute)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", signature ?? StripeWebhookSigner.Sign(payload));

        return _client.SendAsync(request, Ct);
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string token)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return _client.SendAsync(request, Ct);
    }

    private static JsonElement Read(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(response.Content.ReadAsStringAsync().GetAwaiter().GetResult(), Json);

    private static JsonElement Data(HttpResponseMessage response) => Read(response).GetProperty("data");

    private static string? Code(HttpResponseMessage response) => Read(response).GetProperty("error").GetProperty("code").GetString();
}
