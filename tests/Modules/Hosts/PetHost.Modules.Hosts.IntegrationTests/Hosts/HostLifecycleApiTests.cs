using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Hosts.IntegrationTests.Hosts;

/// <summary>
/// O anfitrião verificado pela API: perfil com a identidade travada, foto, inativar/reativar,
/// e as ações do admin (suspender, tirar a suspensão, liberar CPF). Usa o anfitrião de
/// demonstração, que o seed cria já aprovado.
/// </summary>
[Collection(HostsApiCollection.Name)]
public sealed class HostLifecycleApiTests(HostsApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string DemoCpf = "123.456.789-09";

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
    public async Task Patch_Should_ChangeThePhoneAndKeepTheIdentity_When_TheFormComesWhole()
    {
        var token = await LoginAsync();

        var response = await SendAsync(HttpMethod.Patch, "/api/v1/hosts/me", token,
            new { fullName = "Dona Cida (demonstração)", birthDate = "1968-03-15", cpf = DemoCpf, phone = "(44) 3222-1111" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(response).GetProperty("user").GetProperty("phone").GetString().Should().Be("4432221111");
    }

    [Fact]
    public async Task Patch_Should_Return422_When_TheVerifiedNameChanges()
    {
        var token = await LoginAsync();

        var response = await SendAsync(HttpMethod.Patch, "/api/v1/hosts/me", token, new { fullName = "Outro Nome" });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(response).Should().Be("HOST_IDENTITY_LOCKED");
    }

    [Fact]
    public async Task Patch_Should_Return400_When_ThePhoneIsInvalid()
    {
        var token = await LoginAsync();

        var response = await SendAsync(HttpMethod.Patch, "/api/v1/hosts/me", token, new { phone = "abc" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(response).Should().Contain("phone");
    }

    [Fact]
    public async Task Avatar_Should_BeStoredInThePublicBucket_When_Uploaded()
    {
        var token = await LoginAsync();

        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(TestImages.PngVariant(21));
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "eu.png");
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/v1/hosts/me/avatar") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var url = Data(response).GetProperty("user").GetProperty("avatarUrl").GetString()!;
        (await fixture.Bucket.ExistsAsync(url)).Should().BeTrue();

        var removed = await SendAsync(HttpMethod.Delete, "/api/v1/hosts/me/avatar", token);
        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.Bucket.ExistsAsync(url)).Should().BeFalse();
    }

    [Fact]
    public async Task Patch_Should_Return404_When_TheAccountWasNotApprovedYet()
    {
        var token = await RegisterHostAccountAsync("nova@exemplo.com");

        var response = await SendAsync(HttpMethod.Patch, "/api/v1/hosts/me", token, new { phone = "44988887777" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(response).Should().Be("HOST_NOT_FOUND");
    }

    // ----- inativar / reativar -----

    [Fact]
    public async Task Deactivate_Should_BlockTheLoginUntilReactivated_When_TheHostLeaves()
    {
        var token = await LoginAsync();

        (await SendAsync(HttpMethod.Post, "/api/v1/hosts/me/deactivate", token)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await SendAsync(HttpMethod.Get, "/api/v1/hosts/me", token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "as sessões caem na hora");
        (await LoginRawAsync()).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var reactivated = await _client.PostAsJsonAsync("/api/v1/hosts/reactivate",
            new { email = HostsApiFixture.DemoHostEmail, password = HostsApiFixture.DemoHostPassword }, Ct);

        reactivated.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(reactivated).GetProperty("host").GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    // ----- admin -----

    [Fact]
    public async Task Suspension_Should_BlockTheHostAndOnlyTheAdminLiftsIt_When_AdminSuspends()
    {
        var hostId = await DemoHostIdAsync();
        var admin = await AdminTokenAsync();

        var suspended = await SendAsync(HttpMethod.Post, $"/api/v1/hosts/{hostId}/suspension", admin, new { reason = "Denúncia em análise" });
        suspended.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(suspended).GetProperty("user").GetProperty("suspensionReason").GetString().Should().Be("Denúncia em análise");

        Code(await LoginRawAsync()).Should().Be("AUTH_ACCOUNT_SUSPENDED");
        var reactivate = await _client.PostAsJsonAsync("/api/v1/hosts/reactivate",
            new { email = HostsApiFixture.DemoHostEmail, password = HostsApiFixture.DemoHostPassword }, Ct);
        reactivate.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a senha não desfaz a decisão do admin");

        (await SendAsync(HttpMethod.Delete, $"/api/v1/hosts/{hostId}/suspension", admin)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginRawAsync()).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task IdentityRelease_Should_FreeTheCpfForTheRealOwner_When_AdminReleasesIt()
    {
        var hostId = await DemoHostIdAsync();
        var admin = await AdminTokenAsync();

        // A dona de verdade do CPF tenta se verificar e esbarra no CPF já usado.
        var real = await RegisterHostAccountAsync("dona.verdadeira@exemplo.com");
        var blocked = await StartAsync(real, DemoCpf);
        blocked.StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await SendAsync(HttpMethod.Post, $"/api/v1/hosts/{hostId}/identity-release", admin, new { reason = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "só de anfitrião suspenso");

        await SendAsync(HttpMethod.Post, $"/api/v1/hosts/{hostId}/suspension", admin, new { reason = "CPF de terceiro" });
        var released = await SendAsync(HttpMethod.Post, $"/api/v1/hosts/{hostId}/identity-release", admin, new { reason = "Documento da titular conferido" });
        released.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(released).GetProperty("identityReleasedAt").ValueKind.Should().Be(JsonValueKind.String);

        (await StartAsync(real, DemoCpf)).StatusCode.Should().Be(HttpStatusCode.Created);

        var lift = await SendAsync(HttpMethod.Delete, $"/api/v1/hosts/{hostId}/suspension", admin);
        lift.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(lift).Should().Be("HOST_IDENTITY_RELEASED");
    }

    [Fact]
    public async Task AdminRoutes_Should_Return403_When_CalledByAHost()
    {
        var token = await LoginAsync();

        (await SendAsync(HttpMethod.Get, "/api/v1/hosts", token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Audit_Should_RecordTheAdminReadAndTheSuspension_When_TheyHappen()
    {
        var hostId = await DemoHostIdAsync();
        var admin = await AdminTokenAsync();
        await SendAsync(HttpMethod.Get, $"/api/v1/hosts/{hostId}", admin);
        await SendAsync(HttpMethod.Post, $"/api/v1/hosts/{hostId}/suspension", admin, new { reason = "Denúncia" });

        var response = await SendAsync(HttpMethod.Get, $"/api/v1/audit-entries?targetType=host&targetId={hostId}", admin);
        var actions = Data(response).GetProperty("items").EnumerateArray().Select(e => e.GetProperty("action").GetString());

        actions.Should().Contain(["host.viewed", "host.suspended"]);
    }

    // ----- helpers -----

    private async Task<Guid> DemoHostIdAsync() =>
        Data(await SendAsync(HttpMethod.Get, "/api/v1/hosts/me", await LoginAsync())).GetProperty("id").GetGuid();

    private Task<HttpResponseMessage> LoginRawAsync() =>
        _client.PostAsJsonAsync("/api/v1/auth/sessions/login",
            new { email = HostsApiFixture.DemoHostEmail, password = HostsApiFixture.DemoHostPassword, role = "host" }, Ct);

    private async Task<string> LoginAsync()
    {
        var response = await LoginRawAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return Data(response).GetProperty("accessToken").GetString()!;
    }

    private async Task<string> AdminTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/sessions/login",
            new { email = HostsApiFixture.AdminEmail, password = HostsApiFixture.AdminPassword, role = "admin" }, Ct);
        return Data(response).GetProperty("accessToken").GetString()!;
    }

    private async Task<string> RegisterHostAccountAsync(string email)
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
        return Data(response).GetProperty("accessToken").GetString()!;
    }

    private Task<HttpResponseMessage> StartAsync(string token, string cpf) =>
        SendAsync(HttpMethod.Post, "/api/v1/hosts/me/application", token, new { cpf, documentType = "cnh", acceptNotice = true });

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);

        return _client.SendAsync(request, Ct);
    }

    private static JsonElement Read(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<JsonElement>(response.Content.ReadAsStringAsync().GetAwaiter().GetResult(), Json);

    private static JsonElement Data(HttpResponseMessage response) => Read(response).GetProperty("data");

    private static string? Code(HttpResponseMessage response) => Read(response).GetProperty("error").GetProperty("code").GetString();

    private static IEnumerable<string?> Fields(HttpResponseMessage response) =>
        Read(response).GetProperty("error").GetProperty("details").EnumerateArray().Select(d => d.GetProperty("field").GetString());
}
