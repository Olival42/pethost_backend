using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Modules.Auth.Infrastructure;
using PetHost.Modules.Auth.Infrastructure.Persistence;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Auth.IntegrationTests.Sessions;

[Collection(AuthApiCollection.Name)]
public sealed class AuthEndpointsTests(AuthApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string LoginRoute = "/api/v1/auth/sessions/login";
    private const string RefreshRoute = "/api/v1/auth/sessions/refresh";
    private const string LogoutRoute = "/api/v1/auth/sessions/logout";

    private HttpClient _client = null!;

    /// <summary>Token do xUnit: cancela as chamadas se o teste for abortado ou estourar o tempo.</summary>
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

    // ----- login -----

    [Fact]
    public async Task Login_Should_Return200WithSession_When_CredentialsAreValid()
    {
        await CreateUserAsync("camila@exemplo.com", "senha-forte-123", "owner");

        var response = await LoginAsync("camila@exemplo.com", "senha-forte-123", "owner");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var session = (await ReadEnvelopeAsync(response)).Data!;
        session.AccessToken.Should().NotBeNullOrWhiteSpace();
        session.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddSeconds(900).ToUnixTimeSeconds(), 30);
        session.User.FullName.Should().Be("Camila Souza");
    }

    [Fact]
    public async Task Login_Should_Return401_When_PasswordIsWrong()
    {
        await CreateUserAsync("camila@exemplo.com", "senha-forte-123", "owner");

        var response = await LoginAsync("camila@exemplo.com", "senha-errada-123", "owner");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadEnvelopeAsync(response)).Error!.Code.Should().Be("AUTH_INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Login_Should_Return401_When_AccountDoesNotExist()
    {
        var response = await LoginAsync("ninguem@exemplo.com", "senha-forte-123", "owner");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadEnvelopeAsync(response)).Error!.Code.Should().Be("AUTH_INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Login_Should_Return401_When_RoleDoesNotMatchTheAccount()
    {
        // Conta criada como tutora; tentar entrar como anfitriã não acha nada.
        await CreateUserAsync("camila@exemplo.com", "senha-forte-123", "owner");

        var response = await LoginAsync("camila@exemplo.com", "senha-forte-123", "host");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_Should_DistinguishAccounts_When_SameEmailHasBothRoles()
    {
        await CreateUserAsync("cida@exemplo.com", "senha-de-tutora-1", "owner");
        await CreateUserAsync("cida@exemplo.com", "senha-de-anfitria-1", "host");

        var asOwner = await LoginAsync("cida@exemplo.com", "senha-de-tutora-1", "owner");
        var asHost = await LoginAsync("cida@exemplo.com", "senha-de-anfitria-1", "host");
        var crossed = await LoginAsync("cida@exemplo.com", "senha-de-tutora-1", "host");

        asOwner.StatusCode.Should().Be(HttpStatusCode.OK);
        asHost.StatusCode.Should().Be(HttpStatusCode.OK);

        // A senha de uma conta não serve na outra.
        crossed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var ownerId = (await ReadEnvelopeAsync(asOwner)).Data!.User.Id;
        var hostId = (await ReadEnvelopeAsync(asHost)).Data!.User.Id;
        ownerId.Should().NotBe(hostId);
    }

    [Fact]
    public async Task Login_Should_IgnoreEmailCasing_When_CredentialsAreValid()
    {
        await CreateUserAsync("camila@exemplo.com", "senha-forte-123", "owner");

        var response = await LoginAsync("  CAMILA@EXEMPLO.COM ", "senha-forte-123", "owner");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_Should_Return400OnRole_When_RoleIsUnknown()
    {
        var response = await LoginAsync("camila@exemplo.com", "senha-forte-123", "tutor");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        FieldsIn(await ReadEnvelopeAsync(response)).Should().Contain("role");
    }

    // ----- seeder do admin -----

    [Fact]
    public async Task Login_Should_Succeed_When_UsingTheSeededAdmin()
    {
        var response = await LoginAsync(
            AuthApiFixture.SeededAdminEmail,
            AuthApiFixture.SeededAdminPassword,
            "admin");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadEnvelopeAsync(response)).Data!.User.Role.Should().Be("admin");
    }

    [Fact]
    public async Task Seeder_Should_BeIdempotent_When_RunAgain()
    {
        // O InitializeAsync já rodou o seed uma vez; rodar de novo não duplica.
        await fixture.Services.InitializeAuthModuleAsync(applyMigrations: false, Ct);

        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        var admins = await dbContext.Users.AsNoTracking().CountAsync(u => u.Role == UserRole.Admin, Ct);

        admins.Should().Be(1);
    }

    // ----- refresh e logout -----

    [Fact]
    public async Task Refresh_Should_Return200WithNewPair_When_TokenIsValid()
    {
        var login = await CreateUserAndLoginAsync();

        var response = await RefreshAsync(login.RefreshToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshed = (await ReadEnvelopeAsync(response)).Data!;
        refreshed.RefreshToken.Should().NotBe(login.RefreshToken);
        refreshed.AccessToken.Should().NotBeNullOrWhiteSpace();
        refreshed.User.Id.Should().Be(login.User.Id);
    }

    [Fact]
    public async Task Refresh_Should_Return401_When_TokenIsReused()
    {
        // Rotação: o token já consumido não vale mais, mesmo dentro do prazo.
        var login = await CreateUserAndLoginAsync();

        var first = await RefreshAsync(login.RefreshToken);
        var second = await RefreshAsync(login.RefreshToken);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadEnvelopeAsync(second)).Error!.Code.Should().Be("AUTH_REFRESH_TOKEN_INVALID");
    }

    [Fact]
    public async Task Refresh_Should_AllowOnlyOneWinner_When_SameTokenIsUsedConcurrently()
    {
        // GETDEL no Redis: duas chamadas paralelas com o mesmo token não podem
        // ambas ter sucesso. É o cenário de um token vazado usado em paralelo.
        var login = await CreateUserAndLoginAsync();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 5).Select(_ => RefreshAsync(login.RefreshToken)));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).Should().Be(4);
    }

    [Fact]
    public async Task Refresh_Should_Return401_When_TokenIsUnknown()
    {
        var response = await RefreshAsync("token-inventado-que-nunca-foi-emitido");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_Should_Return400_When_TokenIsMissing()
    {
        var response = await PostAsync(RefreshRoute, new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        FieldsIn(await ReadEnvelopeAsync(response)).Should().Contain("refreshToken");
    }

    [Fact]
    public async Task Revoke_Should_InvalidateRefreshToken_When_LoggingOut()
    {
        var login = await CreateUserAndLoginAsync();

        var revoke = await PostAsync(LogoutRoute, new { refreshToken = login.RefreshToken });
        var refreshAfter = await RefreshAsync(login.RefreshToken);

        revoke.StatusCode.Should().Be(HttpStatusCode.OK);
        refreshAfter.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Revoke_Should_Return200_When_TokenWasAlreadyInvalid()
    {
        var response = await PostAsync(LogoutRoute, new { refreshToken = "token-que-nunca-existiu" });

        // Logout é idempotente: o estado desejado pelo cliente já vale.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ----- token emitido -----

    [Fact]
    public async Task AccessToken_Should_PassTheHostValidation_When_IssuedByLogin()
    {
        // Valida com os TokenValidationParameters que o próprio host registrou
        // no JwtBearer: mesma chave, issuer, audience e claim de papel.
        var login = await CreateUserAndLoginAsync();

        var result = await ValidateWithHostAsync(login.AccessToken);

        result.IsValid.Should().BeTrue();
        result.ClaimsIdentity.FindFirst("sub")!.Value.Should().Be(login.User.Id.ToString());
        // RoleClaimType = "role" no host: é o que faz [Authorize(Roles = "owner")] funcionar.
        new ClaimsPrincipal(result.ClaimsIdentity).IsInRole("owner").Should().BeTrue();
    }

    [Fact]
    public async Task AccessToken_Should_FailTheHostValidation_When_Tampered()
    {
        var login = await CreateUserAndLoginAsync();
        var parts = login.AccessToken.Split('.');

        // Troca o payload por outro (papel admin) mantendo a assinatura original.
        var forgedPayload = Base64UrlEncoder.Encode("""{"sub":"1","role":"admin"}""");
        var forged = $"{parts[0]}.{forgedPayload}.{parts[2]}";

        var result = await ValidateWithHostAsync(forged);

        result.IsValid.Should().BeFalse();
    }

    private async Task<TokenValidationResult> ValidateWithHostAsync(string token)
    {
        var parameters = fixture.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme)
            .TokenValidationParameters;

        return await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);
    }

    // ----- corpo inválido -----

    [Theory]
    [InlineData(LoginRoute, """{ "email": "a@b.com", "password": "x", "role": "admin": }""")]
    [InlineData(LoginRoute, """{ "email": "a@b.com", """)]
    [InlineData(LoginRoute, "isto nao e json")]
    [InlineData(RefreshRoute, """{ "refreshToken": """)]
    public async Task AnyEndpoint_Should_Return400_When_BodyIsMalformedJson(string route, string body)
    {
        // Antes desta correção o JSON inválido chegava ao handler como null e
        // a API respondia 500.
        var response = await PostRawAsync(route, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var envelope = await ReadEnvelopeAsync(response);
        envelope.Error!.Code.Should().Be("VALIDATION_ERROR");
        FieldsIn(envelope).Should().Equal("body");
    }

    [Fact]
    public async Task AnyEndpoint_Should_Return400_When_BodyIsEmpty()
    {
        var response = await PostRawAsync(LoginRoute, string.Empty);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        FieldsIn(await ReadEnvelopeAsync(response)).Should().Equal("body");
    }

    [Fact]
    public async Task MalformedBody_Should_NotLeakInternalTypeNames_When_Rejected()
    {
        var response = await PostRawAsync(LoginRoute, """{ "email": 123 }""");
        var body = await response.Content.ReadAsStringAsync(Ct);

        body.Should().NotContain("PetHost.");
        body.Should().NotContain("System.");
    }

    // ----- contrato de resposta -----

    [Fact]
    public async Task EveryResponse_Should_UseTheEnvelope_When_RequestFails()
    {
        var response = await LoginAsync("ninguem@exemplo.com", "senha-forte-123", "owner");
        var body = await response.Content.ReadAsStringAsync(Ct);

        body.Should().Contain("\"success\":false");
        body.Should().Contain("\"timestamp\"");
        body.Should().Contain("\"error\"");

        // Nulo omitido (§7): `data` não aparece quando é null.
        body.Should().NotContain("\"data\":null");
    }

    [Fact]
    public async Task Health_Should_ReportReady_When_PostgresAndRedisAreUp()
    {
        var response = await _client.GetAsync("/health/ready", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ----- helpers -----

    private Task<HttpResponseMessage> PostAsync(string route, object payload) =>
        _client.PostAsJsonAsync(route, payload, Ct);

    private Task<HttpResponseMessage> PostRawAsync(string route, string body) =>
        _client.PostAsync(route, new StringContent(body, System.Text.Encoding.UTF8, "application/json"), Ct);

    private Task<HttpResponseMessage> LoginAsync(string email, string password, string role) =>
        PostAsync(LoginRoute, new { email, password, role });

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        PostAsync(RefreshRoute, new { refreshToken });

    /// <summary>
    /// Cria a conta direto no banco: o cadastro pela API está desligado por enquanto,
    /// então o login é testado contra usuários montados pelo próprio domínio.
    /// </summary>
    private async Task CreateUserAsync(string email, string password, string role)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = new UserBuilder()
            .WithFullName(role == "host" ? "Dona Cida" : "Camila Souza")
            .WithEmail(email)
            .WithPasswordHash(passwordHasher.Hash(password))
            .WithRole(role == "host" ? UserRole.Host : UserRole.Owner)
            .WithCreatedAt(DateTimeOffset.UtcNow)
            .Build();

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(Ct);
    }

    private async Task<SessionPayload> CreateUserAndLoginAsync()
    {
        await CreateUserAsync("camila@exemplo.com", "senha-forte-123", "owner");

        var response = await LoginAsync("camila@exemplo.com", "senha-forte-123", "owner");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await ReadEnvelopeAsync(response)).Data!;
    }

    private static async Task<Envelope> ReadEnvelopeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<Envelope>(Json, Ct))!;

    private static List<string?> FieldsIn(Envelope envelope) =>
        envelope.Error?.Details is { ValueKind: JsonValueKind.Array } details
            ? [.. details.EnumerateArray().Select(d => d.GetProperty("field").GetString())]
            : [];

    private sealed record Envelope(bool Success, SessionPayload? Data, ErrorPayload? Error, DateTimeOffset Timestamp);

    private sealed record ErrorPayload(string Code, string Message, JsonElement? Details);

    private sealed record SessionPayload(
        string AccessToken,
        string RefreshToken,
        long ExpiresAt,
        UserPayload User);

    private sealed record UserPayload(Guid Id, string FullName, string Email, string Role);
}
