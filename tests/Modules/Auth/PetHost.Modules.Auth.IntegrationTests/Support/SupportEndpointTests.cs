using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace PetHost.Modules.Auth.IntegrationTests.Support;

/// <summary>
/// <c>GET /api/v1/support</c>: o e-mail do suporte, público, para o app mostrar quando a API manda
/// falar com o suporte (CPF já usado por outra conta). Fica aqui porque é rota do host, como o health.
/// </summary>
[Collection(AuthApiCollection.Name)]
public sealed class SupportEndpointTests(AuthApiFixture fixture)
{
    private const string Route = "/api/v1/support";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Support_Should_ReturnTheConfiguredEmailWithoutLogin_When_ItIsSet()
    {
        // Arrange
        await using var api = fixture.WithSettings(new Dictionary<string, string> { ["Support:Email"] = "  suporte@pethost.test  " });
        using var client = api.CreateClient();

        // Act
        var response = await client.GetAsync(Route, Ct);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("data").GetProperty("email").GetString().Should().Be("suporte@pethost.test");
    }

    [Fact]
    public async Task Support_Should_OmitTheEmail_When_ItIsNotConfigured()
    {
        // Arrange
        using var client = fixture.CreateClient();

        // Act
        var response = await client.GetAsync(Route, Ct);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("data").TryGetProperty("email", out _).Should().BeFalse("nulo é omitido no JSON da API");
    }
}
