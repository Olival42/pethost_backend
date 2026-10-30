using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Hosts.Infrastructure;
using PetHost.Modules.Hosts.Infrastructure.Persistence;
using Xunit;

namespace PetHost.Modules.Hosts.IntegrationTests.HostApplications;

/// <summary>O anfitrião de demonstração (apresentação): criado já verificado e com o Stripe ligado.</summary>
[Collection(HostsApiCollection.Name)]
public sealed class DemoHostSeedTests(HostsApiFixture fixture) : IAsyncLifetime
{
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

    [Fact]
    public async Task Seed_Should_CreateAVerifiedHostWithStripe_When_Enabled()
    {
        var login = await _client.PostAsJsonAsync(
            "/api/v1/auth/sessions/login",
            new { email = HostsApiFixture.DemoHostEmail, password = HostsApiFixture.DemoHostPassword, role = "host" },
            Ct);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var token = Read(await login.Content.ReadAsStringAsync(Ct)).GetProperty("data").GetProperty("accessToken").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/hosts/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var me = await _client.SendAsync(request, Ct);

        me.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = Read(await me.Content.ReadAsStringAsync(Ct)).GetProperty("data");
        data.GetProperty("cpf").GetString().Should().Be("12345678909");
        data.GetProperty("stripeConnected").GetBoolean().Should().BeTrue();

        await using var scope = fixture.Services.CreateAsyncScope();
        var host = await scope.ServiceProvider.GetRequiredService<HostsDbContext>().Hosts.AsNoTracking().SingleAsync(Ct);
        host.StripeAccountId.Should().Be(HostsApiFixture.DemoHostStripeAccount);
    }

    [Fact]
    public async Task Seed_Should_DoNothing_When_TheDemoHostAlreadyExists()
    {
        // Rodar de novo (outra subida da API) não duplica nada.
        await fixture.Services.InitializeHostsModuleAsync(applyMigrations: false, Ct);

        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HostsDbContext>();
        (await db.Hosts.CountAsync(Ct)).Should().Be(1);
        (await db.HostApplications.CountAsync(Ct)).Should().Be(1);
    }

    private static JsonElement Read(string body) => JsonSerializer.Deserialize<JsonElement>(body, Json);
}
