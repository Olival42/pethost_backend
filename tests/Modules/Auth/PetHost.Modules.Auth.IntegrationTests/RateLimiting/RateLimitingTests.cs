using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace PetHost.Modules.Auth.IntegrationTests.RateLimiting;

/// <summary>
/// Rate limit ponta a ponta, numa instância da API com o limite ligado e baixo: as
/// políticas críticas estouram antes do limite geral, a resposta é <c>429</c> no
/// envelope com <c>Retry-After</c>, e o health check fica de fora.
/// </summary>
[Collection(AuthApiCollection.Name)]
public sealed class RateLimitingTests(AuthApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private WebApplicationFactory<Program> _api = null!;
    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();

        _api = fixture.WithSettings(new Dictionary<string, string>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:Global:PermitLimit"] = "8",
            ["RateLimiting:Global:WindowSeconds"] = "60",
            ["RateLimiting:Policies:credentials:PermitLimit"] = "3",
            ["RateLimiting:Policies:credentials:WindowSeconds"] = "60",
            ["RateLimiting:Policies:password-reset:PermitLimit"] = "2",
            ["RateLimiting:Policies:password-reset:WindowSeconds"] = "60",
        });
        _client = _api.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _api.DisposeAsync();
    }

    [Fact]
    public async Task Login_Should_Return429InTheEnvelope_When_CredentialsLimitIsExceeded()
    {
        for (var i = 0; i < 3; i++)
            (await LoginAsync("Senha@Errada1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var blocked = await LoginAsync(AuthApiFixture.SeededAdminPassword);

        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "nem a senha certa passa depois do limite");
        blocked.Headers.RetryAfter!.Delta.Should().BePositive();
        var body = await blocked.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        body.GetProperty("success").GetBoolean().Should().BeFalse();
        body.GetProperty("error").GetProperty("code").GetString().Should().Be("TOO_MANY_REQUESTS");
    }

    [Fact]
    public async Task ForgotPassword_Should_HaveItsOwnLimit_When_LoginIsStillAvailable()
    {
        for (var i = 0; i < 2; i++)
            (await ForgotAsync()).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ForgotAsync()).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await LoginAsync(AuthApiFixture.SeededAdminPassword)).StatusCode
            .Should().Be(HttpStatusCode.OK, "cada política tem a própria contagem");
    }

    [Fact]
    public async Task GlobalLimit_Should_CountPerUser_When_LoggedIn()
    {
        var login = await LoginAsync(AuthApiFixture.SeededAdminPassword);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>(Json, Ct))
            .GetProperty("data").GetProperty("accessToken").GetString()!;

        // O login gastou 1 do limite geral do IP; logado, a contagem é do usuário.
        for (var i = 0; i < 8; i++)
            (await GetMeAsync(token)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await GetMeAsync(token)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task HealthCheck_Should_NeverBeLimited_When_CalledManyTimes()
    {
        for (var i = 0; i < 12; i++)
            (await _client.GetAsync("/health/live", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private Task<HttpResponseMessage> LoginAsync(string password) =>
        _client.PostAsJsonAsync(
            "/api/v1/auth/sessions/login",
            new { email = AuthApiFixture.SeededAdminEmail, password, role = "admin" },
            Ct);

    private Task<HttpResponseMessage> ForgotAsync() =>
        _client.PostAsJsonAsync("/api/v1/auth/password/forgot", new { email = "ninguem@exemplo.com", role = "owner" }, Ct);

    private Task<HttpResponseMessage> GetMeAsync(string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request, Ct);
    }
}
