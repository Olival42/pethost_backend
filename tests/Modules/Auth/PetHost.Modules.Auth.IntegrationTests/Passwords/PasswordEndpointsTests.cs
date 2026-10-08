using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Modules.Auth.Infrastructure.Persistence;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Auth.IntegrationTests.Passwords;

[Collection(AuthApiCollection.Name)]
public sealed partial class PasswordEndpointsTests(AuthApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string ForgotRoute = "/api/v1/auth/password/forgot";
    private const string ResetRoute = "/api/v1/auth/password/reset";
    private const string LoginRoute = "/api/v1/auth/sessions/login";
    private const string RefreshRoute = "/api/v1/auth/sessions/refresh";

    private const string Email = "camila@exemplo.com";
    private const string OldPassword = "senha-antiga-123";
    private const string NewPassword = "Nova@Senha123";

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

    // ----- esqueci a senha -----

    [Fact]
    public async Task Forgot_Should_EmailATokenAndLink_When_AccountExists()
    {
        await CreateOwnerAsync();

        var response = await ForgotAsync(Email, "owner");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var email = (await fixture.Emails.WaitForAsync(Email, 1, Ct)).Should().ContainSingle().Subject;
        email.Subject.Should().Contain("redefinição de senha");
        email.ToName.Should().Be("Camila Souza");
        email.TextBody.Should().Contain("https://app.pethost.test/redefinir-senha?token=");
        TokenIn(email.TextBody).Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Forgot_Should_Return200WithoutEmail_When_AccountDoesNotExist()
    {
        // Mesma resposta da conta existente: o endpoint não revela quem tem cadastro.
        var response = await ForgotAsync("ninguem@exemplo.com", "owner");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.Emails.WaitForAsync("ninguem@exemplo.com", 1, Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Forgot_Should_Return200WithoutEmail_When_RoleDoesNotMatch()
    {
        await CreateOwnerAsync();

        var response = await ForgotAsync(Email, "host");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.Emails.WaitForAsync(Email, 1, Ct)).Should().BeEmpty();
    }

    [Fact]
    public async Task Forgot_Should_Return400_When_PayloadIsEmpty()
    {
        var response = await _client.PostAsJsonAsync(ForgotRoute, new { }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        FieldsIn(await ReadEnvelopeAsync(response)).Should().Contain(["email", "role"]);
    }

    // ----- troca de senha -----

    [Fact]
    public async Task Reset_Should_ChangePassword_When_TokenAndPasswordAreValid()
    {
        await CreateOwnerAsync();
        var token = await RequestTokenAsync();

        var response = await ResetAsync(token, NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(NewPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync(OldPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reset_Should_RevokeEveryOpenSession_When_PasswordChanges()
    {
        await CreateOwnerAsync();
        var phone = await RefreshTokenFromLoginAsync();
        var laptop = await RefreshTokenFromLoginAsync();
        var token = await RequestTokenAsync();

        (await ResetAsync(token, NewPassword)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await RefreshAsync(phone)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(laptop)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reset_Should_Return401_When_TokenIsReused()
    {
        await CreateOwnerAsync();
        var token = await RequestTokenAsync();
        (await ResetAsync(token, NewPassword)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await ResetAsync(token, "Outra@Senha456");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadEnvelopeAsync(response)).Error!.Code.Should().Be("AUTH_PASSWORD_RESET_TOKEN_INVALID");
    }

    [Fact]
    public async Task Reset_Should_Return401_When_TokenIsUnknown()
    {
        var response = await ResetAsync("token-que-nunca-existiu", NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadEnvelopeAsync(response)).Error!.Code.Should().Be("AUTH_PASSWORD_RESET_TOKEN_INVALID");
    }

    [Fact]
    public async Task Reset_Should_InvalidateOlderToken_When_ANewOneIsRequested()
    {
        await CreateOwnerAsync();
        var first = await RequestTokenAsync(expectedEmails: 1);
        var second = await RequestTokenAsync(expectedEmails: 2);

        (await ResetAsync(first, NewPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ResetAsync(second, NewPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reset_Should_Return400WithEveryBrokenRule_When_PasswordIsWeak()
    {
        await CreateOwnerAsync();
        var token = await RequestTokenAsync();

        var response = await ResetAsync(token, "fraca");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var details = (await ReadEnvelopeAsync(response)).Error!.Details!.Value;
        var messages = details.EnumerateArray()
            .Single(d => d.GetProperty("field").GetString() == "newPassword")
            .GetProperty("messages").EnumerateArray().Select(m => m.GetString()).ToList();

        messages.Should().HaveCount(4);
        messages.Should().Contain(m => m!.Contains("uppercase"));
        messages.Should().Contain(m => m!.Contains("special character"));
    }

    [Fact]
    public async Task Reset_Should_KeepTokenUsable_When_PasswordWasRejected()
    {
        // Senha fraca é barrada antes do token ser consumido: a pessoa corrige e
        // tenta de novo sem pedir outro e-mail.
        await CreateOwnerAsync();
        var token = await RequestTokenAsync();

        (await ResetAsync(token, "fraca")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ResetAsync(token, NewPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ----- helpers -----

    private Task<HttpResponseMessage> ForgotAsync(string email, string role) =>
        _client.PostAsJsonAsync(ForgotRoute, new { email, role }, Ct);

    private Task<HttpResponseMessage> ResetAsync(string token, string newPassword) =>
        _client.PostAsJsonAsync(ResetRoute, new { token, newPassword }, Ct);

    private Task<HttpResponseMessage> LoginAsync(string password) =>
        _client.PostAsJsonAsync(LoginRoute, new { email = Email, password, role = "owner" }, Ct);

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        _client.PostAsJsonAsync(RefreshRoute, new { refreshToken }, Ct);

    private async Task<string> RefreshTokenFromLoginAsync()
    {
        var response = await LoginAsync(OldPassword);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);
        return body.GetProperty("data").GetProperty("refreshToken").GetString()!;
    }

    /// <summary>Pede o "esqueci a senha" e lê o token do e-mail, como a pessoa faria.</summary>
    private async Task<string> RequestTokenAsync(int expectedEmails = 1)
    {
        (await ForgotAsync(Email, "owner")).StatusCode.Should().Be(HttpStatusCode.OK);

        var emails = await fixture.Emails.WaitForAsync(Email, expectedEmails, Ct);
        emails.Should().HaveCount(expectedEmails);

        return TokenIn(emails[^1].TextBody)!;
    }

    private static string? TokenIn(string body)
    {
        var match = TokenPattern().Match(body);
        return match.Success ? match.Groups["token"].Value : null;
    }

    [GeneratedRegex(@"código: (?<token>[A-Za-z0-9_-]+)")]
    private static partial Regex TokenPattern();

    private async Task CreateOwnerAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        dbContext.Users.Add(new UserBuilder()
            .WithEmail(Email)
            .WithPasswordHash(passwordHasher.Hash(OldPassword))
            .WithRole(UserRole.Owner)
            .WithCreatedAt(DateTimeOffset.UtcNow)
            .Build());

        await dbContext.SaveChangesAsync(Ct);
    }

    private static async Task<Envelope> ReadEnvelopeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<Envelope>(Json, Ct))!;

    private static List<string?> FieldsIn(Envelope envelope) =>
        envelope.Error?.Details is { ValueKind: JsonValueKind.Array } details
            ? [.. details.EnumerateArray().Select(d => d.GetProperty("field").GetString())]
            : [];

    private sealed record Envelope(bool Success, ErrorPayload? Error);

    private sealed record ErrorPayload(string Code, string Message, JsonElement? Details);
}
