using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace PetHost.Modules.Conversations.IntegrationTests.Conversations;

/// <summary>
/// O hub <c>/hubs/conversations</c> com o cliente oficial do SignalR, por WebSocket e com o token na
/// query (<c>access_token</c>), como o navegador faz: a mensagem enviada pela API chega na hora ao
/// outro lado, com o contador de não lidas, e conexão sem token é recusada.
/// </summary>
[Collection(ConversationsApiCollection.Name)]
public sealed class ConversationsHubTests(ConversationsApiFixture fixture) : IAsyncLifetime
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly List<HubConnection> _connections = [];
    private HttpClient _client = null!;
    private ConversationsScenario _scenario = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        _client = fixture.CreateClient();
        _scenario = new ConversationsScenario(fixture, _client);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
            await connection.DisposeAsync();

        _client.Dispose();
    }

    [Fact]
    public async Task Hub_Should_PushTheMessageAndTheUnreadCountToTheHost_When_TheOwnerSends()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        var host = await ConnectAsync(pair.HostToken);
        var message = Next(host, "messageCreated");
        var unread = Next(host, "unreadCountChanged");

        // Act
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Oi, Dona Cida!");

        // Assert
        var pushed = await message.WaitAsync(Wait, Ct);
        pushed.GetProperty("body").GetString().Should().Be("Oi, Dona Cida!");
        pushed.GetProperty("sentBy").GetString().Should().Be("owner");
        pushed.GetProperty("conversationId").GetGuid().Should().Be(conversationId);
        (await unread.WaitAsync(Wait, Ct)).GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Hub_Should_EchoTheMessageToTheSenderOtherConnections_When_TheOwnerSends()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        var ownerOtherTab = await ConnectAsync(pair.OwnerToken);
        var message = Next(ownerOtherTab, "messageCreated");

        // Act
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Mandei do celular");

        // Assert
        (await message.WaitAsync(Wait, Ct)).GetProperty("body").GetString().Should().Be("Mandei do celular");
    }

    [Fact]
    public async Task Hub_Should_PushTheNewTotal_When_TheReaderMarksTheConversationAsRead()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Oi!");
        var hostOtherTab = await ConnectAsync(pair.HostToken);
        var unread = Next(hostOtherTab, "unreadCountChanged");

        // Act
        await _scenario.SendAsync(HttpMethod.Put, $"{ConversationsScenario.ConversationsRoute}/{conversationId}/read-marker", pair.HostToken);

        // Assert
        (await unread.WaitAsync(Wait, Ct)).GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Hub_Should_RefuseTheConnection_When_ThereIsNoToken()
    {
        // Act
        var connect = () => ConnectAsync(token: null);

        // Assert
        await connect.Should().ThrowAsync<Exception>();
    }

    /// <summary>Conecta por WebSocket no servidor em memória, com o token na query, como o navegador.</summary>
    private async Task<HubConnection> ConnectAsync(string? token)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(fixture.Server.BaseAddress, "hubs/conversations"), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = async (context, cancellationToken) =>
                {
                    var uri = token is null
                        ? context.Uri
                        : new UriBuilder(context.Uri) { Query = $"access_token={Uri.EscapeDataString(token)}" }.Uri;

                    return await fixture.Server.CreateWebSocketClient().ConnectAsync(uri, cancellationToken);
                };
            })
            .Build();

        _connections.Add(connection);
        await connection.StartAsync(Ct);

        return connection;
    }

    /// <summary>O próximo evento <paramref name="eventName"/> que a conexão receber.</summary>
    private static Task<JsonElement> Next(HubConnection connection, string eventName)
    {
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<JsonElement>(eventName, payload => received.TrySetResult(payload));

        return received.Task;
    }
}
