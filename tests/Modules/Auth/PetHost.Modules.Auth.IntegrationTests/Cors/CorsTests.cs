using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace PetHost.Modules.Auth.IntegrationTests.Cors;

/// <summary>
/// CORS do host: o front (Next.js) chama a API de outra origem. Fica aqui porque é configuração do
/// host, como o health e o suporte. A origem liberada no teste é <see cref="Front"/>.
/// </summary>
[Collection(AuthApiCollection.Name)]
public sealed class CorsTests(AuthApiFixture fixture) : IAsyncLifetime
{
    private const string Front = "https://app.pethost.test";

    private WebApplicationFactory<Program> _api = null!;
    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _api = fixture.WithSettings(new Dictionary<string, string> { ["Cors:AllowedOrigins:0"] = Front + "/" });
        _client = _api.CreateClient();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _api.DisposeAsync();
    }

    [Fact]
    public async Task Preflight_Should_AllowTheFrontWithCredentials_When_OriginIsConfigured()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/users/me");
        request.Headers.Add("Origin", Front);
        request.Headers.Add("Access-Control-Request-Method", "PATCH");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        // Act
        var response = await _client.SendAsync(request, Ct);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        Header(response, "Access-Control-Allow-Origin").Should().Be(Front);
        Header(response, "Access-Control-Allow-Credentials").Should().Be("true");
        Header(response, "Access-Control-Allow-Methods").Should().Contain("PATCH");
        Header(response, "Access-Control-Allow-Headers").Should().ContainAll("authorization", "content-type");
        Header(response, "Access-Control-Max-Age").Should().Be("600");
    }

    [Fact]
    public async Task Request_Should_CarryCorsHeadersAndExposeLocation_When_OriginIsConfigured()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/support");
        request.Headers.Add("Origin", Front);

        // Act
        var response = await _client.SendAsync(request, Ct);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        Header(response, "Access-Control-Allow-Origin").Should().Be(Front);
        Header(response, "Access-Control-Expose-Headers").Should().ContainAll("Location", "Retry-After");
    }

    [Fact]
    public async Task Request_Should_CarryCorsHeaders_When_TheApiAnswers401()
    {
        // Arrange: o front precisa ler o 401 para renovar o token.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/users/me");
        request.Headers.Add("Origin", Front);

        // Act
        var response = await _client.SendAsync(request, Ct);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Header(response, "Access-Control-Allow-Origin").Should().Be(Front);
    }

    [Fact]
    public async Task Request_Should_NotCarryCorsHeaders_When_OriginIsNotConfigured()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/support");
        request.Headers.Add("Origin", "https://evil.example");
        request.Headers.Add("Access-Control-Request-Method", "GET");

        // Act
        var response = await _client.SendAsync(request, Ct);

        // Assert
        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : string.Empty;
}
