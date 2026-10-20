using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Api.Seed;
using PetHost.Modules.Hosts.Infrastructure.Persistence;
using PetHost.Modules.Listings.Infrastructure.Persistence;
using PetHost.Modules.Owners.Infrastructure.Persistence;
using PetHost.Modules.Pets.Infrastructure.Persistence;
using Xunit;

namespace PetHost.Modules.Bookings.IntegrationTests.Bookings;

/// <summary>Os dados de teste (<see cref="TestDataSeeder"/>): tutores com pets e anfitriões com cantinho na busca.</summary>
[Collection(BookingsApiCollection.Name)]
public sealed class TestDataSeedTests(BookingsApiFixture fixture) : IAsyncLifetime
{
    private const string Password = "Teste@1234";

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
    public async Task Seed_Should_CreateOwnersWithPetsAndHostsWithListings()
    {
        await SeedAsync();

        var owner = TestDataCatalog.Owners[0];
        var ownerToken = await LoginAsync(owner.Person.Email, "owner");
        var pets = await GetDataAsync("/api/v1/pets/me", ownerToken);
        pets.GetProperty("pets").EnumerateArray().Select(p => p.GetProperty("name").GetString())
            .Should().BeEquivalentTo(owner.Pets.Select(p => p.Name));

        // Os repasses de mentira deixam os cantinhos na busca.
        var search = await GetDataAsync("/api/v1/listings?latitude=-23.41&longitude=-51.933&radiusKm=10", ownerToken);
        search.GetProperty("items").EnumerateArray().Select(l => l.GetProperty("title").GetString())
            .Should().BeEquivalentTo(TestDataCatalog.Hosts.Select(h => h.Listing.Title));

        var host = TestDataCatalog.Hosts[0];
        var hostToken = await LoginAsync(host.Person.Email, "host");
        var mine = await GetDataAsync("/api/v1/listings/me", hostToken);
        mine.GetProperty("listings").GetArrayLength().Should().Be(1);

        var housePets = await GetDataAsync("/api/v1/pets/me", hostToken);
        housePets.GetProperty("pets").GetArrayLength().Should().Be(host.Pets.Count);
    }

    [Fact]
    public async Task Seed_Should_DoNothing_When_RunAgain()
    {
        await SeedAsync();
        await SeedAsync();

        await using var scope = fixture.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        (await services.GetRequiredService<OwnersDbContext>().Owners.CountAsync(Ct)).Should().Be(TestDataCatalog.Owners.Count);
        (await services.GetRequiredService<HostsDbContext>().Hosts.CountAsync(Ct)).Should().Be(TestDataCatalog.Hosts.Count);
        (await services.GetRequiredService<ListingsDbContext>().Listings.CountAsync(Ct)).Should().Be(TestDataCatalog.Hosts.Count);
        (await services.GetRequiredService<PetsDbContext>().Pets.CountAsync(Ct))
            .Should().Be(TestDataCatalog.Owners.Sum(o => o.Pets.Count) + TestDataCatalog.Hosts.Sum(h => h.Pets.Count));
    }

    private async Task SeedAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TestDataSeeder>().SeedAsync(Password, Ct);
    }

    private async Task<string> LoginAsync(string email, string role)
    {
        var login = await _client.PostAsJsonAsync("/api/v1/auth/sessions/login", new { email, password = Password, role }, Ct);
        login.StatusCode.Should().Be(HttpStatusCode.OK);

        return Read(await login.Content.ReadAsStringAsync(Ct)).GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    private async Task<JsonElement> GetDataAsync(string url, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        return Read(body).GetProperty("data");
    }

    private static JsonElement Read(string body) => JsonSerializer.Deserialize<JsonElement>(body, Json);
}
