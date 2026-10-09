using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Modules.Auth.Infrastructure.Persistence;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Auth.IntegrationTests.Users;

/// <summary>
/// Conta do usuário ponta a ponta: perfil, troca de senha, inativar e reativar (pelo contrato), contas
/// da mesma pessoa e troca entre tutor e anfitrião.
/// </summary>
/// <remarks>
/// Conta de tutor nasce no módulo Owners (com CPF); aqui ela é montada direto no banco,
/// e <c>/users/register</c> é exercitado com anfitrião.
/// </remarks>
[Collection(AuthApiCollection.Name)]
public sealed class AccountEndpointsTests(AuthApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string Email = "cida@exemplo.com";
    private const string OwnerPassword = "Tutora@123";
    private const string HostPassword = "Anfitria@456";

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

    // ----- perfil -----

    [Fact]
    public async Task GetMe_Should_ReturnTheAccountOfTheToken_When_LoggedIn()
    {
        var session = await RegisterAsync("owner", OwnerPassword);

        var response = await SendAsync(HttpMethod.Get, "/api/v1/users/me", session.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = (await ReadAsync(response)).GetProperty("data");
        user.GetProperty("id").GetGuid().Should().Be(session.UserId);
        user.GetProperty("email").GetString().Should().Be(Email);
        user.GetProperty("address").GetProperty("city").GetString().Should().Be("Maringá");
    }

    [Fact]
    public async Task GetMe_Should_Return401_When_NotLoggedIn()
    {
        (await _client.GetAsync("/api/v1/users/me", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ----- troca de senha -----

    [Fact]
    public async Task ChangePassword_Should_EndOldSessionsAndReturnANewOne_When_CurrentPasswordIsRight()
    {
        var session = await RegisterAsync("host", HostPassword);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/users/me/password", session.AccessToken,
            new { currentPassword = HostPassword, newPassword = "Outra@Senha789" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var newToken = (await ReadAsync(response)).GetProperty("data").GetProperty("accessToken").GetString()!;

        (await SendAsync(HttpMethod.Get, "/api/v1/users/me", session.AccessToken)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "o token antigo cai na hora");
        (await RefreshAsync(session.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await SendAsync(HttpMethod.Get, "/api/v1/users/me", newToken)).StatusCode
            .Should().Be(HttpStatusCode.OK, "a sessão nova é emitida depois da revogação");

        (await LoginAsync("host", HostPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await LoginAsync("host", "Outra@Senha789")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangePassword_Should_Return400OnCurrentPasswordAndKeepSession_When_PasswordIsWrong()
    {
        var session = await RegisterAsync("host", HostPassword);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/users/me/password", session.AccessToken,
            new { currentPassword = "Errada@123", newPassword = "Outra@Senha789" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Equal("currentPassword");
        (await SendAsync(HttpMethod.Get, "/api/v1/users/me", session.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangePassword_Should_Return400OnNewPassword_When_ItIsWeakOrTheSame()
    {
        var session = await RegisterAsync("host", HostPassword);

        var weak = await SendAsync(HttpMethod.Post, "/api/v1/users/me/password", session.AccessToken,
            new { currentPassword = HostPassword, newPassword = "fraca" });
        var same = await SendAsync(HttpMethod.Post, "/api/v1/users/me/password", session.AccessToken,
            new { currentPassword = HostPassword, newPassword = HostPassword });

        Fields(await ReadAsync(weak)).Should().OnlyContain(f => f == "newPassword");
        Fields(await ReadAsync(same)).Should().Equal("newPassword");
    }

    [Fact]
    public async Task ChangePassword_Should_WorkForOwnerAccounts_When_LoggedIn()
    {
        var owner = await RegisterAsync("owner", OwnerPassword);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/users/me/password", owner.AccessToken,
            new { currentPassword = OwnerPassword, newPassword = "Outra@Senha789" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ----- validação do cadastro -----

    [Fact]
    public async Task Register_Should_ReturnEveryInvalidField_When_ManyFieldsAreEmpty()
    {
        // Data vazia não derruba a leitura do corpo: vira erro de campo, junto com os outros.
        var response = await _client.PostAsJsonAsync("/api/v1/users/register", new
        {
            fullName = "",
            email = "",
            password = "",
            phone = "",
            birthDate = "",
            address = new { zipCode = "87020-000", street = "", number = "120", complement = "Apto 3", neighborhood = "Zona 7", city = "Maringá", state = "PR" },
        }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().BeEquivalentTo(
            ["fullName", "email", "password", "phone", "birthDate", "address.street"]);
    }

    [Theory]
    [InlineData("10/05/1990")]
    [InlineData("1990-13-40")]
    public async Task Register_Should_ReportBirthDateField_When_DateIsMalformed(string birthDate)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/users/register", new { birthDate }, Ct);

        var body = await ReadAsync(response);
        Fields(body).Should().Contain("birthDate").And.NotContain("body");
    }

    [Fact]
    public async Task Register_Should_PointToTheField_When_ValueHasWrongType()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/users/register", new { fullName = "Cida", phone = 44999990000 }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Equal("phone");
    }

    // ----- inativar e reativar (pelo contrato que o módulo do papel usa) -----

    [Fact]
    public async Task Deactivate_Should_BlockLoginAndRefreshOfThatAccountOnly_When_Called()
    {
        await RegisterAsync("owner", OwnerPassword);
        var host = await RegisterAsync("host", HostPassword);

        (await DeactivateAsync(host.UserId)).IsSuccess.Should().BeTrue();

        var hostLogin = await LoginAsync("host", HostPassword);
        hostLogin.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await ReadAsync(hostLogin)).Should().Be("AUTH_ACCOUNT_DEACTIVATED");

        (await RefreshAsync(host.RefreshToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await LoginAsync("owner", OwnerPassword)).StatusCode.Should()
            .Be(HttpStatusCode.OK, "a conta de tutor da mesma pessoa continua ativa");
    }

    [Fact]
    public async Task Deactivate_Should_RejectTheOldAccessTokenImmediately_When_Called()
    {
        // Sem esperar os 15 min de vida do token.
        var host = await RegisterAsync("host", HostPassword);
        (await SendAsync(HttpMethod.Get, "/api/v1/users/me", host.AccessToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        await DeactivateAsync(host.UserId);

        var response = await SendAsync(HttpMethod.Get, "/api/v1/users/me", host.AccessToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Code(await ReadAsync(response)).Should().Be("UNAUTHORIZED");
    }

    [Fact]
    public async Task Register_Should_PointLocationToMe_When_Created()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/users/register", new
        {
            fullName = "Dona Cida",
            email = Email,
            password = HostPassword,
            phone = "(44) 99999-0000",
            birthDate = "1965-03-20",
            address = new { zipCode = "87020-000", street = "Rua das Flores", number = "120", neighborhood = "Zona 7", city = "Maringá", state = "PR" },
        }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location!.ToString().Should().Be("/api/v1/users/me");
    }

    [Fact]
    public async Task Login_Should_Return401NotDeactivated_When_PasswordIsWrongOnInactiveAccount()
    {
        // "Conta inativa" só aparece com a senha certa: não vira oráculo de cadastro.
        var host = await RegisterAsync("host", HostPassword);
        await DeactivateAsync(host.UserId);

        (await LoginAsync("host", "Senha@Errada1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reactivate_Should_ReopenAccountAndReturnSession_When_CredentialsAreValid()
    {
        var host = await RegisterAsync("host", HostPassword);
        await DeactivateAsync(host.UserId);

        var result = await ReactivateAsync(HostPassword);

        result.IsSuccess.Should().BeTrue();
        result.Value!.User.IsActive.Should().BeTrue();
        (await LoginAsync("host", HostPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reactivate_Should_FailWithInvalidCredentials_When_PasswordIsWrong()
    {
        var host = await RegisterAsync("host", HostPassword);
        await DeactivateAsync(host.UserId);

        var result = await ReactivateAsync("Senha@Errada1");

        result.FirstError!.Code.Should().Be("AUTH_INVALID_CREDENTIALS");
        (await LoginAsync("host", HostPassword)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ----- contas da mesma pessoa -----

    [Fact]
    public async Task Accounts_Should_ListBothRolesAndMarkCurrent_When_PersonHasTwoAccounts()
    {
        var owner = await RegisterAsync("owner", OwnerPassword);
        await RegisterAsync("host", HostPassword);

        var response = await SendAsync(HttpMethod.Get, "/api/v1/users/me/accounts", owner.AccessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var accounts = (await ReadAsync(response)).GetProperty("data").EnumerateArray()
            .Select(a => (Role: a.GetProperty("role").GetString(), Current: a.GetProperty("isCurrent").GetBoolean()))
            .ToList();

        accounts.Should().BeEquivalentTo([("owner", true), ("host", false)]);
    }

    [Fact]
    public async Task Switch_Should_OpenSessionOnTheOtherAccount_When_PasswordIsRight()
    {
        var owner = await RegisterAsync("owner", OwnerPassword);
        var host = await RegisterAsync("host", HostPassword);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/auth/sessions/switch", owner.AccessToken,
            new { role = "host", password = HostPassword });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = (await ReadAsync(response)).GetProperty("data").GetProperty("user");
        user.GetProperty("id").GetGuid().Should().Be(host.UserId);
        user.GetProperty("role").GetString().Should().Be("host");
    }

    [Fact]
    public async Task Switch_Should_Return404_When_PasswordIsWrong()
    {
        // Mesmo e-mail não prova que é a mesma pessoa enquanto o e-mail não é verificado.
        var owner = await RegisterAsync("owner", OwnerPassword);
        await RegisterAsync("host", HostPassword);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/auth/sessions/switch", owner.AccessToken,
            new { role = "host", password = OwnerPassword });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(response)).Should().Be("AUTH_LINKED_ACCOUNT_NOT_FOUND");
    }

    [Fact]
    public async Task Switch_Should_Return404_When_OtherAccountDoesNotExist()
    {
        var owner = await RegisterAsync("owner", OwnerPassword);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/auth/sessions/switch", owner.AccessToken,
            new { role = "host", password = OwnerPassword });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ----- helpers -----

    private sealed record Session(string AccessToken, string RefreshToken, Guid UserId);

    /// <summary>
    /// Anfitrião pelo cadastro da API; tutor direto no banco (o cadastro de tutor é do
    /// módulo Owners) e depois login, para ter a sessão.
    /// </summary>
    private async Task<Session> RegisterAsync(string role, string password)
    {
        if (role == "owner")
            return await SeedOwnerAndLoginAsync(password);

        var response = await _client.PostAsJsonAsync("/api/v1/users/register", new
        {
            fullName = "Dona Cida",
            email = Email,
            password,
            phone = "(44) 99999-0000",
            birthDate = "1965-03-20",
            address = new { zipCode = "87020-000", street = "Rua das Flores", number = "120", neighborhood = "Zona 7", city = "Maringá", state = "PR" },
        }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var data = (await ReadAsync(response)).GetProperty("data");

        return new Session(
            data.GetProperty("accessToken").GetString()!,
            data.GetProperty("refreshToken").GetString()!,
            data.GetProperty("user").GetProperty("id").GetGuid());
    }

    private async Task<Session> SeedOwnerAndLoginAsync(string password)
    {
        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

            dbContext.Users.Add(new UserBuilder()
                .WithFullName("Cida Tutora")
                .WithEmail(Email)
                .WithPasswordHash(passwordHasher.Hash(password))
                .WithRole(UserRole.Owner)
                .WithCreatedAt(DateTimeOffset.UtcNow)
                .Build());

            await dbContext.SaveChangesAsync(Ct);
        }

        var response = await LoginAsync("owner", password);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await ReadAsync(response)).GetProperty("data");

        return new Session(
            data.GetProperty("accessToken").GetString()!,
            data.GetProperty("refreshToken").GetString()!,
            data.GetProperty("user").GetProperty("id").GetGuid());
    }

    /// <summary>Inativa pelo contrato <c>IAccountStatusManager</c>: não há mais rota em <c>/users</c>.</summary>
    private async Task<Result> DeactivateAsync(Guid userId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAccountStatusManager>().DeactivateAsync(userId, Ct);
    }

    private async Task<Result<AccountSession>> ReactivateAsync(string password)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAccountStatusManager>().ReactivateAsync(Email, password, "host", Ct);
    }

    private Task<HttpResponseMessage> LoginAsync(string role, string password) =>
        _client.PostAsJsonAsync("/api/v1/auth/sessions/login", new { email = Email, password, role }, Ct);

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        _client.PostAsJsonAsync("/api/v1/auth/sessions/refresh", new { refreshToken }, Ct);

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

    private static string? Code(JsonElement body) => body.GetProperty("error").GetProperty("code").GetString();

    private static List<string?> Fields(JsonElement body) =>
        body.GetProperty("error").TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
            ? [.. details.EnumerateArray().Select(d => d.GetProperty("field").GetString())]
            : [];
}
