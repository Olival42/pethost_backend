using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;
using static PetHost.Modules.Conversations.IntegrationTests.ConversationsScenario;

namespace PetHost.Modules.Conversations.IntegrationTests.Conversations;

/// <summary>
/// O limite próprio do chat (<c>chat-messages</c>) numa instância da API com o rate limit ligado e
/// baixo: texto e foto contam juntos, por conta, e a resposta é <c>429</c> no envelope com
/// <c>Retry-After</c>. Ler continua liberado.
/// </summary>
[Collection(ConversationsApiCollection.Name)]
public sealed class ConversationsRateLimitingTests(ConversationsApiFixture fixture) : IAsyncLifetime
{
    private const int ChatLimit = 3;

    private WebApplicationFactory<Program> _api = null!;
    private HttpClient _client = null!;
    private ConversationsScenario _scenario = null!;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        _api = fixture.WithSettings(new Dictionary<string, string>
        {
            ["RateLimiting:Enabled"] = "true",
            ["RateLimiting:Global:PermitLimit"] = "1000",
            ["RateLimiting:Global:WindowSeconds"] = "60",
            ["RateLimiting:Policies:chat-messages:PermitLimit"] = ChatLimit.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RateLimiting:Policies:chat-messages:WindowSeconds"] = "60",
        });
        _client = _api.CreateClient();
        _scenario = new ConversationsScenario(fixture, _client);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _api.DisposeAsync();
    }

    [Fact]
    public async Task Send_Should_Return429InTheEnvelope_When_TextAndPhotosGoOverTheChatLimit()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Oi!");
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Tudo bem?");
        (await _scenario.SendPhotoAsync(pair.OwnerToken, conversationId, "Essa é a Pipoca")).StatusCode.Should().Be(HttpStatusCode.Created);

        // Act
        var blocked = await _scenario.SendAsync(HttpMethod.Post, $"{ConversationsRoute}/{conversationId}/messages", pair.OwnerToken, new { body = "Mais uma" });

        // Assert
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        blocked.Headers.RetryAfter!.Delta.Should().BePositive();
        var body = await ReadAsync(blocked);
        body.GetProperty("success").GetBoolean().Should().BeFalse();
        Code(body).Should().Be("TOO_MANY_REQUESTS");
    }

    [Fact]
    public async Task ChatLimit_Should_CountPerAccountAndLeaveReadingFree_When_OneSideIsBlocked()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        var route = $"{ConversationsRoute}/{conversationId}/messages";
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "1");
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "2");
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "3");

        // Act
        var ownerAgain = await _scenario.SendAsync(HttpMethod.Post, route, pair.OwnerToken, new { body = "4" });
        var hostReply = await _scenario.SendAsync(HttpMethod.Post, route, pair.HostToken, new { body = "Oi, Camila!" });
        var ownerReads = await _scenario.SendAsync(HttpMethod.Get, route, pair.OwnerToken);

        // Assert
        ownerAgain.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        hostReply.StatusCode.Should().Be(HttpStatusCode.Created, "cada conta tem a própria contagem");
        ownerReads.StatusCode.Should().Be(HttpStatusCode.OK, "ler não conta no limite do chat");
    }
}
