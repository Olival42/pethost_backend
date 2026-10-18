using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Modules.Hosts.Infrastructure.Persistence;
using Xunit;

namespace PetHost.Modules.Hosts.IntegrationTests.Hosts;

/// <summary>
/// Exclusão da conta de anfitrião pela API (LGPD): o anfitrião de demonstração (aprovado) e uma
/// conta que ainda está na verificação.
/// </summary>
[Collection(HostsApiCollection.Name)]
public sealed class HostDeletionApiTests(HostsApiFixture fixture) : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string Password = "Nova@Senha123";

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
    public async Task DeleteMe_Should_DropTheCpfAndEndTheSessions_When_TheApprovedHostLeaves()
    {
        var token = await LoginAsync();
        var hostId = Data(await SendAsync(HttpMethod.Get, "/api/v1/hosts/me", token)).GetProperty("id").GetGuid();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/hosts/me/deletion", token, new { currentPassword = HostsApiFixture.DemoHostPassword });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await SendAsync(HttpMethod.Get, "/api/v1/hosts/me", token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "as sessões caem na hora");
        (await LoginRawAsync()).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "o e-mail não é mais de conta nenhuma");

        await using var scope = fixture.Services.CreateAsyncScope();
        var hosts = scope.ServiceProvider.GetRequiredService<HostsDbContext>();
        var host = await hosts.Hosts.AsNoTracking().SingleAsync(h => h.Id == new HostId(hostId), Ct);
        host.Cpf.Should().BeNull();
        host.IsActive.Should().BeFalse();
        host.ErasedAt.Should().NotBeNull();
        (await hosts.HostApplications.AsNoTracking().AnyAsync(a => a.UserId == host.UserId, Ct)).Should().BeFalse("a solicitação sai com os documentos");
    }

    [Fact]
    public async Task DeleteMe_Should_EraseTheApplication_When_TheAccountIsStillInVerification()
    {
        var token = await RegisterHostAccountAsync("maria@exemplo.com");
        (await SendAsync(HttpMethod.Post, "/api/v1/hosts/me/application", token, new { cpf = "529.982.247-25", documentType = "cnh", acceptNotice = true }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await SendAsync(HttpMethod.Post, "/api/v1/hosts/me/deletion", token, new { currentPassword = Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = fixture.Services.CreateAsyncScope();
        var cpf = Cpf.Create("52998224725").Value!;
        (await scope.ServiceProvider.GetRequiredService<HostsDbContext>().HostApplications.AsNoTracking().AnyAsync(a => a.Cpf == cpf, Ct))
            .Should().BeFalse("o anfitrião de demonstração tem a dele; a da Maria saiu");

        // O CPF ficou livre: outra conta (outro e-mail) pode usá-lo.
        var other = await RegisterHostAccountAsync("outra@exemplo.com");
        (await SendAsync(HttpMethod.Post, "/api/v1/hosts/me/application", other, new { cpf = "529.982.247-25", documentType = "cnh", acceptNotice = true }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task DeleteMe_Should_Return400AndKeepTheHost_When_PasswordIsWrong()
    {
        var token = await LoginAsync();

        var response = await SendAsync(HttpMethod.Post, "/api/v1/hosts/me/deletion", token, new { currentPassword = "Errada@123" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Read(response).GetProperty("error").GetProperty("details")[0].GetProperty("field").GetString().Should().Be("currentPassword");
        (await SendAsync(HttpMethod.Get, "/api/v1/hosts/me", token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ----- helpers -----

    private Task<HttpResponseMessage> LoginRawAsync() =>
        _client.PostAsJsonAsync("/api/v1/auth/sessions/login",
            new { email = HostsApiFixture.DemoHostEmail, password = HostsApiFixture.DemoHostPassword, role = "host" }, Ct);

    private async Task<string> LoginAsync()
    {
        var response = await LoginRawAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return Data(response).GetProperty("accessToken").GetString()!;
    }

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
        return Data(response).GetProperty("accessToken").GetString()!;
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

    private static JsonElement Data(HttpResponseMessage response) => Read(response).GetProperty("data");
}
