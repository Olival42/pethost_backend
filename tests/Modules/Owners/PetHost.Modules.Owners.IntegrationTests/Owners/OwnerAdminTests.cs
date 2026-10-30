using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace PetHost.Modules.Owners.IntegrationTests.Owners;

/// <summary>
/// Ações do admin sobre um tutor — suspender, tirar a suspensão, liberar o CPF — e a
/// trilha de auditoria, ponta a ponta pela API.
/// </summary>
[Collection(OwnersApiCollection.Name)]
public sealed class OwnerAdminTests(OwnersApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string OwnersRoute = "/api/v1/owners";
    private const string AuditRoute = "/api/v1/audit-entries";
    private const string Password = "Nova@Senha123";
    private const string Cpf = "529.982.247-25";

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

    // ----- suspensão -----

    [Fact]
    public async Task Suspend_Should_BlockTheOwnerImmediately_When_AdminSuspends()
    {
        var owner = await RegisterOwnerAsync("camila@exemplo.com", Cpf);
        var admin = await AdminTokenAsync();

        var response = await SendAsync(HttpMethod.Post, $"{OwnersRoute}/{owner.OwnerId}/suspension", admin, new { reason = "Uso de CPF de terceiro" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await ReadAsync(response)).GetProperty("data");
        data.GetProperty("suspendedAt").ValueKind.Should().Be(JsonValueKind.String);
        data.GetProperty("user").GetProperty("suspensionReason").GetString().Should().Be("Uso de CPF de terceiro");

        (await SendAsync(HttpMethod.Get, $"{OwnersRoute}/me", owner.Token)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "as sessões caem na hora");

        var login = await LoginAsync("camila@exemplo.com");
        login.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await ReadAsync(login)).Should().Be("AUTH_ACCOUNT_SUSPENDED");
    }

    [Fact]
    public async Task Reactivate_Should_NotLiftASuspension_When_OwnerTriesWithThePassword()
    {
        // A diferença entre inativar e suspender: a senha não desfaz a decisão do admin.
        var owner = await RegisterOwnerAsync("camila@exemplo.com", Cpf);
        await SuspendAsync(owner.OwnerId, await AdminTokenAsync());

        var response = await _client.PostAsJsonAsync($"{OwnersRoute}/reactivate", new { email = "camila@exemplo.com", password = Password }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await ReadAsync(response)).Should().Be("AUTH_ACCOUNT_SUSPENDED");
    }

    [Fact]
    public async Task LiftSuspension_Should_LetTheOwnerSignInAgain_When_AdminLiftsIt()
    {
        var owner = await RegisterOwnerAsync("camila@exemplo.com", Cpf);
        var admin = await AdminTokenAsync();
        await SuspendAsync(owner.OwnerId, admin);

        var response = await SendAsync(HttpMethod.Delete, $"{OwnersRoute}/{owner.OwnerId}/suspension", admin);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync("camila@exemplo.com")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Suspend_Should_Return400_When_ReasonIsMissing()
    {
        var owner = await RegisterOwnerAsync("camila@exemplo.com", Cpf);

        var response = await SendAsync(HttpMethod.Post, $"{OwnersRoute}/{owner.OwnerId}/suspension", await AdminTokenAsync(), new { reason = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Equal("reason");
    }

    [Fact]
    public async Task AdminRoutes_Should_Return403_When_CalledByAnOwner()
    {
        var owner = await RegisterOwnerAsync("camila@exemplo.com", Cpf);

        (await SendAsync(HttpMethod.Post, $"{OwnersRoute}/{owner.OwnerId}/suspension", owner.Token, new { reason = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await SendAsync(HttpMethod.Get, AuditRoute, owner.Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ----- disputa de CPF -----

    [Fact]
    public async Task ReleaseCpf_Should_LetTheRealOwnerRegister_When_AdminReleasesItFromTheFraud()
    {
        var fraud = await RegisterOwnerAsync("golpista@exemplo.com", Cpf);
        var admin = await AdminTokenAsync();

        // A dona de verdade tenta se cadastrar e esbarra no CPF já usado.
        var blocked = await _client.PostAsJsonAsync($"{OwnersRoute}/register", OwnerAccount("dona@exemplo.com", Cpf), Ct);
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);

        await SuspendAsync(fraud.OwnerId, admin);
        var released = await SendAsync(HttpMethod.Post, $"{OwnersRoute}/{fraud.OwnerId}/cpf-release", admin, new { reason = "Documento da titular conferido" });

        released.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(released)).GetProperty("data").TryGetProperty("cpf", out _).Should().BeFalse("o CPF foi liberado");

        (await _client.PostAsJsonAsync($"{OwnersRoute}/register", OwnerAccount("dona@exemplo.com", Cpf), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var lift = await SendAsync(HttpMethod.Delete, $"{OwnersRoute}/{fraud.OwnerId}/suspension", admin);
        lift.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(lift)).Should().Be("OWNER_CPF_RELEASED");
    }

    [Fact]
    public async Task ReleaseCpf_Should_Return422_When_OwnerIsNotSuspended()
    {
        var owner = await RegisterOwnerAsync("camila@exemplo.com", Cpf);

        var response = await SendAsync(HttpMethod.Post, $"{OwnersRoute}/{owner.OwnerId}/cpf-release", await AdminTokenAsync(), new { reason = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(await ReadAsync(response)).Should().Be("OWNER_SUSPENSION_REQUIRED");
    }

    // ----- trilha de auditoria -----

    [Fact]
    public async Task Audit_Should_RecordWhoSuspendedAndWhy_When_AdminSuspends()
    {
        var owner = await RegisterOwnerAsync("camila@exemplo.com", Cpf);
        var admin = await AdminTokenAsync();
        await SuspendAsync(owner.OwnerId, admin);

        var entries = await AuditAsync(admin, $"?targetType=owner&targetId={owner.OwnerId}");

        var suspended = entries.Single(e => e.GetProperty("action").GetString() == "owner.suspended");
        suspended.GetProperty("reason").GetString().Should().Be("Uso de CPF de terceiro");
        suspended.GetProperty("actorRole").GetString().Should().Be("admin");
        entries.Select(e => e.GetProperty("action").GetString()).Should().Contain("owner.registered");
    }

    [Fact]
    public async Task Audit_Should_RecordLoginsAndAdminReads_When_TheyHappen()
    {
        var owner = await RegisterOwnerAsync("camila@exemplo.com", Cpf);
        await _client.PostAsJsonAsync("/api/v1/auth/sessions/login", new { email = "camila@exemplo.com", password = "Errada@123", role = "owner" }, Ct);
        var admin = await AdminTokenAsync();
        await SendAsync(HttpMethod.Get, $"{OwnersRoute}/{owner.OwnerId}", admin);

        var failed = await AuditAsync(admin, $"?action=session.login_failed&targetId={owner.UserId}");
        failed.Should().ContainSingle().Which.GetProperty("details").GetProperty("reason").GetString().Should().Be("invalid_credentials");

        var viewed = await AuditAsync(admin, $"?action=owner.viewed&targetId={owner.OwnerId}");
        viewed.Should().ContainSingle("o admin abriu o CPF completo de outra pessoa (LGPD)");
    }

    [Fact]
    public async Task Audit_Should_NeverStoreTheFullCpf_When_CpfChanges()
    {
        var owner = await RegisterOwnerAsync("camila@exemplo.com", Cpf);
        await SendAsync(HttpMethod.Patch, $"{OwnersRoute}/me", owner.Token, new { cpf = "111.444.777-35", currentPassword = Password });

        var entries = await AuditAsync(await AdminTokenAsync(), $"?action=owner.updated&targetId={owner.OwnerId}");

        var cpf = entries.Should().ContainSingle().Subject.GetProperty("details").GetProperty("cpf").GetString();
        cpf.Should().Be("***.***.777-35");
    }

    [Fact]
    public async Task Audit_Should_Return400_When_PageSizeIsTooBig()
    {
        var response = await SendAsync(HttpMethod.Get, $"{AuditRoute}?pageSize=1000", await AdminTokenAsync());

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Equal("pageSize");
    }

    // ----- helpers -----

    private sealed record RegisteredOwner(string Token, Guid UserId, Guid OwnerId);

    private async Task<RegisteredOwner> RegisterOwnerAsync(string email, string cpf)
    {
        var response = await _client.PostAsJsonAsync($"{OwnersRoute}/register", OwnerAccount(email, cpf), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var data = (await ReadAsync(response)).GetProperty("data");
        var owner = data.GetProperty("owner");
        return new RegisteredOwner(data.GetProperty("accessToken").GetString()!, owner.GetProperty("userId").GetGuid(), owner.GetProperty("id").GetGuid());
    }

    private async Task SuspendAsync(Guid ownerId, string admin) =>
        (await SendAsync(HttpMethod.Post, $"{OwnersRoute}/{ownerId}/suspension", admin, new { reason = "Uso de CPF de terceiro" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

    private async Task<List<JsonElement>> AuditAsync(string admin, string query)
    {
        var response = await SendAsync(HttpMethod.Get, AuditRoute + query, admin);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return [.. (await ReadAsync(response)).GetProperty("data").GetProperty("items").EnumerateArray()];
    }

    private Task<HttpResponseMessage> LoginAsync(string email) =>
        _client.PostAsJsonAsync("/api/v1/auth/sessions/login", new { email, password = Password, role = "owner" }, Ct);

    private async Task<string> AdminTokenAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/sessions/login",
            new { email = OwnersApiFixture.AdminEmail, password = OwnersApiFixture.AdminPassword, role = "admin" },
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return (await ReadAsync(response)).GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        return _client.SendAsync(request, Ct);
    }

    private static object OwnerAccount(string email, string cpf) =>
        new
        {
            fullName = "Camila Souza",
            email,
            password = Password,
            phone = "(44) 99999-0000",
            birthDate = "1990-05-10",
            cpf,
            address = new { zipCode = "87020-000", street = "Rua das Flores", number = "120", neighborhood = "Zona 7", city = "Maringá", state = "PR" },
        };

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json, Ct);

    private static string? Code(JsonElement body) => body.GetProperty("error").GetProperty("code").GetString();

    private static List<string?> Fields(JsonElement body) =>
        body.GetProperty("error").TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array
            ? [.. details.EnumerateArray().Select(d => d.GetProperty("field").GetString())]
            : [];
}
