using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace PetHost.Modules.Conversations.IntegrationTests.Conversations;

/// <summary>
/// O hub <c>/hubs/conversations</c> com o cliente oficial do SignalR, por WebSocket e com o token na
/// query (<c>access_token</c>), como o navegador faz: a mensagem enviada pela API chega na hora ao
/// outro lado, com o contador de não lidas, e conexão sem token é recusada. Com o backplane no
/// Redis, o evento chega também a quem está conectado em outra instância da API.
/// </summary>
[Collection(ConversationsApiCollection.Name)]
public sealed class ConversationsHubTests(ConversationsApiFixture fixture) : IAsyncLifetime
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private readonly List<HubConnection> _connections = [];
    private WebApplicationFactory<Program>? _otherInstance;
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

        if (_otherInstance is not null)
            await _otherInstance.DisposeAsync();

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
    public async Task Hub_Should_PushTheMessageThroughTheRedisBackplane_When_TheHostIsConnectedToAnotherInstance()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        _otherInstance = fixture.WithSettings(new Dictionary<string, string>());
        var host = await ConnectAsync(pair.HostToken, _otherInstance.Server);
        var message = Next(host, "messageCreated");
        var unread = Next(host, "unreadCountChanged");

        // Act: a mensagem é gravada pela instância principal; o anfitrião está só na outra.
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Chegou pela outra instância?");

        // Assert
        (await message.WaitAsync(Wait, Ct)).GetProperty("body").GetString().Should().Be("Chegou pela outra instância?");
        (await unread.WaitAsync(Wait, Ct)).GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Hub_Should_PushTheBlockToTheOtherSide_When_TheHostBlocksAndUnblocks()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        var owner = await ConnectAsync(pair.OwnerToken);
        var changes = new System.Collections.Concurrent.ConcurrentQueue<JsonElement>();
        var twoChanges = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        owner.On<JsonElement>("conversationBlockChanged", payload =>
        {
            changes.Enqueue(payload);
            if (changes.Count == 2)
                twoChanges.TrySetResult();
        });
        var block = $"{ConversationsScenario.ConversationsRoute}/{conversationId}/block";

        // Act
        await _scenario.SendAsync(HttpMethod.Put, block, pair.HostToken);
        await _scenario.SendAsync(HttpMethod.Delete, block, pair.HostToken);

        // Assert
        await twoChanges.Task.WaitAsync(Wait, Ct);
        changes.Select(c => c.GetProperty("blocked").GetBoolean()).Should().Equal(true, false);
        changes.Should().OnlyContain(c => c.GetProperty("conversationId").GetGuid() == conversationId);
    }

    [Fact]
    public async Task Hub_Should_RefuseTheConnection_When_ThereIsNoToken()
    {
        // Act
        var connect = () => ConnectAsync(token: null);

        // Assert
        await connect.Should().ThrowAsync<Exception>();
    }

    /// <summary>
    /// Conecta por WebSocket no servidor em memória (o da fixture, ou <paramref name="server"/>), com
    /// o token na query, como o navegador.
    /// </summary>
    private async Task<HubConnection> ConnectAsync(string? token, TestServer? server = null)
    {
        server ??= fixture.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/conversations"), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = async (context, cancellationToken) =>
                {
                    var uri = token is null
                        ? context.Uri
                        : new UriBuilder(context.Uri) { Query = $"access_token={Uri.EscapeDataString(token)}" }.Uri;

                    return await server.CreateWebSocketClient().ConnectAsync(uri, cancellationToken);
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
