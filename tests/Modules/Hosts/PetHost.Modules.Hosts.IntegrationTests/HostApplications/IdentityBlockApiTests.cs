using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Hosts.Infrastructure.Persistence;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Hosts.IntegrationTests.HostApplications;

/// <summary>
/// Bloqueio do CPF pela API: recusa por fraude (ou a terceira recusa) bloqueia o CPF para qualquer
/// conta, e o bloqueio sobrevive à exclusão da conta; o admin libera.
/// </summary>
[Collection(HostsApiCollection.Name)]
public sealed class IdentityBlockApiTests(HostsApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string ApplicationRoute = "/api/v1/hosts/me/application";
    private const string ReviewRoute = "/api/v1/host-applications";
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

    [Fact]
    public async Task Fraud_Should_BlockTheCpfEvenAfterTheAccountIsDeleted_When_ThePersonComesBack()
    {
        var admin = await AdminTokenAsync();
        var token = await RegisterHostAccountAsync("fraude@exemplo.com");
        var applicationId = await SubmitCompleteApplicationAsync(token);
        (await SendAsync(HttpMethod.Post, $"{ReviewRoute}/{applicationId}/rejection", admin, new { rejectionReason = "suspected_fraud", reason = "Documento adulterado" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var block = await scope.ServiceProvider.GetRequiredService<HostsDbContext>().IdentityBlocks.AsNoTracking().SingleAsync(Ct);
            block.CpfFingerprint.Should().HaveLength(64).And.NotContain("52998224725", "só a impressão do CPF fica guardada");
        }

        (await SendAsync(HttpMethod.Post, "/api/v1/hosts/me/deletion", token, new { currentPassword = Password })).StatusCode.Should().Be(HttpStatusCode.OK);

        // A mesma pessoa volta com o mesmo e-mail: a conta é nova, mas o CPF continua bloqueado.
        var again = await RegisterHostAccountAsync("fraude@exemplo.com");
        var start = await StartAsync(again, Cpf);
        start.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        Code(start).Should().Be("HOST_IDENTITY_BLOCKED");

        // Outro e-mail com o mesmo CPF também.
        var other = await StartAsync(await RegisterHostAccountAsync("outra@exemplo.com"), "52998224725");
        Code(other).Should().Be("HOST_IDENTITY_BLOCKED");
    }

    [Fact]
    public async Task Release_Should_LetTheRealOwnerStart_When_TheAdminReleasesTheCpf()
    {
        var admin = await AdminTokenAsync();
        var fraudster = await RegisterHostAccountAsync("fraude@exemplo.com");
        var applicationId = await SubmitCompleteApplicationAsync(fraudster);
        await SendAsync(HttpMethod.Post, $"{ReviewRoute}/{applicationId}/rejection", admin, new { rejectionReason = "suspected_fraud", reason = "Documento de outra pessoa" });

        var owner = await RegisterHostAccountAsync("dona.do.cpf@exemplo.com");
        Code(await StartAsync(owner, Cpf)).Should().Be("HOST_IDENTITY_BLOCKED");

        var released = await SendAsync(HttpMethod.Post, $"{ReviewRoute}/identity-blocks/release", admin, new { cpf = Cpf, reason = "Documento da dona conferido pelo suporte" });
        released.StatusCode.Should().Be(HttpStatusCode.OK);

        (await StartAsync(owner, Cpf)).StatusCode.Should().Be(HttpStatusCode.Created);

        // A conta do fraudador continua bloqueada pelo histórico dela.
        Code(await StartAsync(fraudster, "111.444.777-35")).Should().Be("HOST_APPLICATION_BLOCKED");

        var again = await SendAsync(HttpMethod.Post, $"{ReviewRoute}/identity-blocks/release", admin, new { cpf = Cpf, reason = "De novo" });
        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(again).Should().Be("HOST_IDENTITY_BLOCK_NOT_FOUND");
    }

    [Fact]
    public async Task Release_Should_Return403_When_NotAdmin()
    {
        var token = await RegisterHostAccountAsync("maria@exemplo.com");

        var response = await SendAsync(HttpMethod.Post, $"{ReviewRoute}/identity-blocks/release", token, new { cpf = Cpf, reason = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Release_Should_Return400_When_CpfOrReasonIsMissing()
    {
        var response = await SendAsync(HttpMethod.Post, $"{ReviewRoute}/identity-blocks/release", await AdminTokenAsync(), new { cpf = "123", reason = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ----- helpers -----

    private async Task<string> RegisterHostAccountAsync(string email)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/users/register", new
        {
            fullName = "Maria Souza",
            email,
            password = Password,
            phone = "(44) 99999-0000",
            birthDate = "1970-01-01",
            address = new { zipCode = "87033-000", street = "Rua A", number = "1", neighborhood = "Centro", city = "Maringá", state = "PR" },
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        return Read(response).GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private Task<HttpResponseMessage> StartAsync(string token, string cpf) =>
        SendAsync(HttpMethod.Post, ApplicationRoute, token, new { cpf, documentType = "cnh", acceptNotice = true });

    private async Task<Guid> SubmitCompleteApplicationAsync(string token)
    {
        (await StartAsync(token, Cpf)).StatusCode.Should().Be(HttpStatusCode.Created);

        var form = new MultipartFormDataContent();
        foreach (var (kind, variant) in new[] { ("front", 1), ("back", 2), ("selfie", 3) })
        {
            var file = new ByteArrayContent(TestImages.PngVariant(variant));
            file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file, kind, $"{kind}.png");
        }

        var upload = new HttpRequestMessage(HttpMethod.Put, $"{ApplicationRoute}/documents") { Content = form };
        upload.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await _client.SendAsync(upload, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var submitted = await SendAsync(HttpMethod.Post, $"{ApplicationRoute}/submission", token);
        submitted.StatusCode.Should().Be(HttpStatusCode.OK);
        return Read(submitted).GetProperty("data").GetProperty("id").GetGuid();
    }

    private async Task<string> AdminTokenAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/sessions/login",
            new { email = HostsApiFixture.AdminEmail, password = HostsApiFixture.AdminPassword, role = "admin" },
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return Read(response).GetProperty("data").GetProperty("accessToken").GetString()!;
    }

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

    private static string? Code(HttpResponseMessage response) => Read(response).GetProperty("error").GetProperty("code").GetString();
}
