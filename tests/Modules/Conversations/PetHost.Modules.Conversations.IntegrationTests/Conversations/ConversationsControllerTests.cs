using System.Net;
using FluentAssertions;
using PetHost.TestKit;
using Xunit;
using static PetHost.Modules.Conversations.IntegrationTests.ConversationsScenario;

namespace PetHost.Modules.Conversations.IntegrationTests.Conversations;

/// <summary>
/// O chat pela API, com Postgres, Redis e o bucket de verdade: abrir conversa, trocar mensagens,
/// não lidas, cursor, foto, a ligação com a estadia, quem vê o quê e o envelope de cada resposta.
/// </summary>
[Collection(ConversationsApiCollection.Name)]
public sealed class ConversationsControllerTests(ConversationsApiFixture fixture) : IAsyncLifetime
{
    private HttpClient _client = null!;
    private ConversationsScenario _scenario = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await fixture.ResetAsync();
        _client = fixture.CreateClient();
        _scenario = new ConversationsScenario(fixture, _client);
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    // ----- abrir conversa -----

    [Fact]
    public async Task Start_Should_Return201AndThenTheSameConversationWith200_When_TheOwnerTapsTwice()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();

        // Act
        var first = await _scenario.SendAsync(HttpMethod.Post, ConversationsRoute, pair.OwnerToken, new { listingId = pair.ListingId });
        var second = await _scenario.SendAsync(HttpMethod.Post, ConversationsRoute, pair.OwnerToken, new { listingId = pair.ListingId });

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await ReadAsync(first);
        created.GetProperty("success").GetBoolean().Should().BeTrue();
        var id = Data(created).GetProperty("id").GetGuid();
        first.Headers.Location!.ToString().Should().Be($"{ConversationsRoute}/{id}");
        Data(created).GetProperty("host").GetProperty("name").GetString().Should().Be("Dona Cida");
        Data(created).GetProperty("unreadCount").GetInt32().Should().Be(0);

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(await ReadAsync(second)).GetProperty("id").GetGuid().Should().Be(id);
    }

    [Fact]
    public async Task Start_Should_CreateASingleConversation_When_TwoRequestsArriveAtTheSameTime()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();

        // Act
        var responses = await Task.WhenAll(
            _scenario.SendAsync(HttpMethod.Post, ConversationsRoute, pair.OwnerToken, new { listingId = pair.ListingId }),
            _scenario.SendAsync(HttpMethod.Post, ConversationsRoute, pair.OwnerToken, new { listingId = pair.ListingId }));

        // Assert
        responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.Created, HttpStatusCode.OK]);
        var ids = await Task.WhenAll(responses.Select(async r => Data(await ReadAsync(r)).GetProperty("id").GetGuid()));
        ids.Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task Start_Should_Return404_When_TheListingDoesNotExist()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();

        // Act
        var response = await _scenario.SendAsync(HttpMethod.Post, ConversationsRoute, pair.OwnerToken, new { listingId = Guid.NewGuid() });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(response)).Should().Be("CONVERSATION_LISTING_NOT_FOUND");
    }

    [Fact]
    public async Task Start_Should_Return403_When_TheTokenIsFromAHost()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();

        // Act
        var response = await _scenario.SendAsync(HttpMethod.Post, ConversationsRoute, pair.HostToken, new { listingId = pair.ListingId });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await ReadAsync(response)).Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task Start_Should_Return401InTheEnvelope_When_ThereIsNoToken()
    {
        // Act
        var response = await _scenario.SendAsync(HttpMethod.Post, ConversationsRoute, token: null, new { listingId = Guid.NewGuid() });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        Code(await ReadAsync(response)).Should().Be("UNAUTHORIZED");
    }

    [Fact]
    public async Task Start_Should_Return400WithTheField_When_TheListingIsMissing()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();

        // Act
        var response = await _scenario.SendAsync(HttpMethod.Post, ConversationsRoute, pair.OwnerToken, new { listingId = Guid.Empty });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Equal("listingId");
    }

    // ----- mensagens e não lidas -----

    [Fact]
    public async Task Messages_Should_ReachTheOtherSideAndClearTheUnreadCount_When_TheHostReads()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);

        // Act
        var sent = await _scenario.SendAsync(HttpMethod.Post, $"{ConversationsRoute}/{conversationId}/messages", pair.OwnerToken, new { body = "  A Pipoca tem medo de fogos?  " });
        var unreadBefore = await _scenario.SendAsync(HttpMethod.Get, $"{ConversationsRoute}/me/unread-count", pair.HostToken);
        var hostList = await _scenario.SendAsync(HttpMethod.Get, $"{ConversationsRoute}/me", pair.HostToken);
        var read = await _scenario.SendAsync(HttpMethod.Put, $"{ConversationsRoute}/{conversationId}/read-marker", pair.HostToken);
        var unreadAfter = await _scenario.SendAsync(HttpMethod.Get, $"{ConversationsRoute}/me/unread-count", pair.HostToken);
        var ownerUnread = await _scenario.SendAsync(HttpMethod.Get, $"{ConversationsRoute}/me/unread-count", pair.OwnerToken);

        // Assert
        sent.StatusCode.Should().Be(HttpStatusCode.Created);
        sent.Headers.Location!.ToString().Should().Be($"{ConversationsRoute}/{conversationId}/messages");
        var message = Data(await ReadAsync(sent));
        message.GetProperty("body").GetString().Should().Be("A Pipoca tem medo de fogos?");
        message.GetProperty("sentBy").GetString().Should().Be("owner");
        message.TryGetProperty("readAt", out _).Should().BeFalse("o remetente não vê se foi lida");
        message.TryGetProperty("imageUrl", out _).Should().BeFalse("nulo é omitido");

        Data(await ReadAsync(unreadBefore)).GetProperty("total").GetInt32().Should().Be(1);
        var item = Data(await ReadAsync(hostList)).GetProperty("items")[0];
        item.GetProperty("unreadCount").GetInt32().Should().Be(1);
        item.GetProperty("lastMessage").GetProperty("body").GetString().Should().Be("A Pipoca tem medo de fogos?");
        item.GetProperty("owner").GetProperty("name").GetString().Should().Be("Camila Souza");

        read.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(await ReadAsync(read)).GetProperty("total").GetInt32().Should().Be(0);
        Data(await ReadAsync(unreadAfter)).GetProperty("total").GetInt32().Should().Be(0);
        Data(await ReadAsync(ownerUnread)).GetProperty("total").GetInt32().Should().Be(0, "a mensagem da própria tutora não conta para ela");
    }

    [Fact]
    public async Task Send_Should_Return400WithTheBodyField_When_TheTextIsBlank()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);

        // Act
        var response = await _scenario.SendAsync(HttpMethod.Post, $"{ConversationsRoute}/{conversationId}/messages", pair.HostToken, new { body = "   " });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await ReadAsync(response);
        Code(body).Should().Be("VALIDATION_ERROR");
        Fields(body).Should().Equal("body");
    }

    [Fact]
    public async Task Messages_Should_PageFromTheNewestWithTheCursor_When_ThereAreMoreThanAPage()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "primeira");
        await _scenario.SendTextAsync(pair.HostToken, conversationId, "segunda");
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "terceira");

        // Act
        var newest = Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"{ConversationsRoute}/{conversationId}/messages?pageSize=2", pair.OwnerToken)));
        var cursor = newest.GetProperty("items")[1].GetProperty("id").GetGuid();
        var older = Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"{ConversationsRoute}/{conversationId}/messages?pageSize=2&before={cursor}", pair.OwnerToken)));

        // Assert
        newest.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("body").GetString()).Should().Equal("terceira", "segunda");
        newest.GetProperty("hasMore").GetBoolean().Should().BeTrue();
        older.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("body").GetString()).Should().Equal("primeira");
        older.GetProperty("hasMore").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Messages_Should_Return400OnBefore_When_TheCursorIsNotAMessageOfTheConversation()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);

        // Act
        var response = await _scenario.SendAsync(HttpMethod.Get, $"{ConversationsRoute}/{conversationId}/messages?before={Guid.NewGuid()}", pair.OwnerToken);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Equal("before");
    }

    [Fact]
    public async Task Conversation_Should_Return404ToEverybodyElse_When_AnotherOwnerTriesToUseIt()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        var stranger = await _scenario.RegisterOwnerAsync("joao@exemplo.com", "111.444.777-35", "João Lima");

        // Act
        var get = await _scenario.SendAsync(HttpMethod.Get, $"{ConversationsRoute}/{conversationId}", stranger);
        var messages = await _scenario.SendAsync(HttpMethod.Get, $"{ConversationsRoute}/{conversationId}/messages", stranger);
        var send = await _scenario.SendAsync(HttpMethod.Post, $"{ConversationsRoute}/{conversationId}/messages", stranger, new { body = "Oi" });

        // Assert
        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
        Code(await ReadAsync(get)).Should().Be("CONVERSATION_NOT_FOUND");
        messages.StatusCode.Should().Be(HttpStatusCode.NotFound);
        send.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Send_Should_Return403_When_TheOwnerIsSuspended()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        await _scenario.SuspendTheOwnerAsync();

        // Act
        var send = await _scenario.SendAsync(HttpMethod.Post, $"{ConversationsRoute}/{conversationId}/messages", pair.OwnerToken, new { body = "Oi" });
        var history = await _scenario.SendAsync(HttpMethod.Get, $"{ConversationsRoute}/{conversationId}/messages", pair.OwnerToken);

        // Assert
        send.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await ReadAsync(send)).Should().Be("CONVERSATION_OWNER_DEACTIVATED");
        history.StatusCode.Should().Be(HttpStatusCode.OK, "suspenso ainda lê o histórico");
    }

    [Fact]
    public async Task Send_Should_Return403RecipientSuspended_When_TheHostWritesToASuspendedOwner()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        await _scenario.SuspendTheOwnerAsync();

        // Act
        var response = await _scenario.SendAsync(HttpMethod.Post, $"{ConversationsRoute}/{conversationId}/messages", pair.HostToken, new { body = "Oi" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await ReadAsync(response)).Should().Be("CONVERSATION_RECIPIENT_SUSPENDED");
    }

    [Fact]
    public async Task SendPhoto_Should_Return403RecipientSuspendedWithoutUploading_When_TheOwnerWritesToASuspendedHost()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        await _scenario.SuspendTheHostAsync();
        var objectsBefore = await fixture.Bucket.CountAsync();

        // Act
        var response = await _scenario.SendPhotoAsync(pair.OwnerToken, conversationId, caption: "Olha a Pipoca");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        Code(await ReadAsync(response)).Should().Be("CONVERSATION_RECIPIENT_SUSPENDED");
        (await fixture.Bucket.CountAsync()).Should().Be(objectsBefore);
    }

    [Fact]
    public async Task Send_Should_Return400OnTheBodyField_When_TheTextHasANullCharacter()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);

        // Act
        var response = await _scenario.SendAsync(HttpMethod.Post, $"{ConversationsRoute}/{conversationId}/messages", pair.OwnerToken, new { body = "antes\0depois" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "o Postgres recusa o nulo em texto: antes virava 500");
        Fields(await ReadAsync(response)).Should().Equal("body");
    }

    [Fact]
    public async Task Send_Should_KeepTheLastActivityAtTheNewestMessage_When_BothSidesSendAtTheSameTime()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        var route = $"{ConversationsRoute}/{conversationId}/messages";

        // Act
        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(i =>
            _scenario.SendAsync(HttpMethod.Post, route, i % 2 == 0 ? pair.OwnerToken : pair.HostToken, new { body = $"rajada {i}" })));

        // Assert
        responses.Select(r => r.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.Created);
        var (lastMessageAt, newestMessage) = await _scenario.LastActivityAsync(conversationId);
        lastMessageAt.Should().Be(newestMessage);
    }

    [Fact]
    public async Task Inbox_Should_ReturnAnEmptyListAndZero_When_TheHostIsNotApprovedYet()
    {
        // Arrange
        var pendingHost = await _scenario.RegisterPendingHostAsync();

        // Act
        var list = await _scenario.SendAsync(HttpMethod.Get, $"{ConversationsRoute}/me", pendingHost);
        var unread = await _scenario.SendAsync(HttpMethod.Get, $"{ConversationsRoute}/me/unread-count", pendingHost);

        // Assert
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = Data(await ReadAsync(list));
        page.GetProperty("items").GetArrayLength().Should().Be(0);
        page.GetProperty("totalCount").GetInt64().Should().Be(0);
        unread.StatusCode.Should().Be(HttpStatusCode.OK);
        Data(await ReadAsync(unread)).GetProperty("total").GetInt32().Should().Be(0);
    }

    // ----- estadia e foto -----

    [Fact]
    public async Task Send_Should_LinkTheMessageToTheStay_When_TheBookingIsInProgress()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        var before = await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Antes da reserva");
        var bookingId = await _scenario.StartStayAsync(pair);

        // Act
        var during = await _scenario.SendTextAsync(pair.HostToken, conversationId, "A Pipoca chegou!");

        // Assert
        before.TryGetProperty("bookingId", out _).Should().BeFalse();
        during.GetProperty("bookingId").GetGuid().Should().Be(bookingId);
    }

    [Fact]
    public async Task SendPhoto_Should_StoreThePhotoInThePrivateBucketWithASignedLink_When_TheImageIsValid()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);

        // Act
        var response = await _scenario.SendPhotoAsync(pair.HostToken, conversationId, "Brincando no quintal");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var message = Data(await ReadAsync(response));
        message.GetProperty("body").GetString().Should().Be("Brincando no quintal");
        var url = message.GetProperty("imageUrl").GetString()!;
        url.Should().Contain("X-Amz-Signature", "a foto do chat só abre por URL assinada");
        message.GetProperty("imageUrlExpiresAt").GetInt64().Should().BeGreaterThan(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var key = TestImageBucket.PrivateKeyOf(url);
        key.Should().StartWith("conversations/");
        (await fixture.Bucket.ListPrivateKeysAsync()).Should().Contain(key);

        using var browser = new HttpClient();
        (await browser.GetAsync(url, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await browser.GetAsync(fixture.Bucket.PrivateObjectUrl(key), TestContext.Current.CancellationToken)).StatusCode
            .Should().Be(HttpStatusCode.Forbidden, "sem a assinatura, o bucket privado recusa");
    }

    [Fact]
    public async Task GetMessagePhoto_Should_ReturnAFreshSignedLink_When_TheRequesterIsInTheConversation()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        var message = Data(await ReadAsync(await _scenario.SendPhotoAsync(pair.HostToken, conversationId, caption: null)));
        var messageId = message.GetProperty("id").GetGuid();
        var stranger = await _scenario.RegisterOwnerAsync("joao@exemplo.com", "111.444.777-35", "João Lima");

        // Act
        var response = await _scenario.SendAsync(HttpMethod.Get, $"{ConversationsRoute}/{conversationId}/messages/{messageId}/photo", pair.OwnerToken);
        var fromStranger = await _scenario.SendAsync(HttpMethod.Get, $"{ConversationsRoute}/{conversationId}/messages/{messageId}/photo", stranger);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        var link = Data(await ReadAsync(response));
        TestImageBucket.PrivateKeyOf(link.GetProperty("url").GetString()!).Should().Be(TestImageBucket.PrivateKeyOf(message.GetProperty("imageUrl").GetString()!));
        fromStranger.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DirectUpload_Should_CreateTheMessageOnlyOnConfirmAndOnlyOnce_When_TheHostSendsAChatPhoto()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        var image = TestImages.PngVariant(7);
        var request = await _scenario.SendAsync(HttpMethod.Post, "/api/v1/uploads", pair.HostToken, new
        {
            purpose = "chat-photo",
            contentType = "image/png",
            sizeBytes = image.Length,
            resourceId = conversationId,
        });
        request.StatusCode.Should().Be(HttpStatusCode.Created);
        var ticket = Data(await ReadAsync(request));
        var uploadId = ticket.GetProperty("uploadId").GetGuid();

        using var browser = new HttpClient();
        var file = new ByteArrayContent(image);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        (await browser.PutAsync(ticket.GetProperty("uploadUrl").GetString(), file, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await MessageCountAsync(pair.OwnerToken, conversationId)).Should().Be(0, "o PUT no bucket não cria mensagem");

        // Act
        var first = await _scenario.SendAsync(HttpMethod.Post, $"/api/v1/uploads/{uploadId}/confirm", pair.HostToken, new { body = "Chegou bem!" });
        var second = await _scenario.SendAsync(HttpMethod.Post, $"/api/v1/uploads/{uploadId}/confirm", pair.HostToken, new { body = "Chegou bem!" });

        // Assert
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var message = Data(await ReadAsync(first)).GetProperty("result");
        message.GetProperty("id").GetGuid().Should().Be(uploadId, "o id da mensagem é o id do upload");
        message.GetProperty("body").GetString().Should().Be("Chegou bem!");
        TestImageBucket.PrivateKeyOf(message.GetProperty("imageUrl").GetString()!).Should().StartWith("conversations/");
        (await MessageCountAsync(pair.OwnerToken, conversationId)).Should().Be(1, "confirmar de novo não duplica a mensagem");
    }

    [Fact]
    public async Task DirectUpload_Should_Return404WithoutSigning_When_TheConversationIsFromSomeoneElse()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        var stranger = await _scenario.RegisterOwnerAsync("joao@exemplo.com", "111.444.777-35", "João Lima");

        // Act
        var response = await _scenario.SendAsync(HttpMethod.Post, "/api/v1/uploads", stranger, new
        {
            purpose = "chat-photo",
            contentType = "image/png",
            sizeBytes = 100,
            resourceId = conversationId,
        });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadAsync(response)).GetProperty("error").GetProperty("code").GetString().Should().Be("CONVERSATION_NOT_FOUND");
    }

    private async Task<int> MessageCountAsync(string token, Guid conversationId) =>
        Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, $"{ConversationsRoute}/{conversationId}/messages", token)))
            .GetProperty("items").GetArrayLength();

    [Fact]
    public async Task SendPhoto_Should_Return404WithoutUploading_When_TheConversationIsFromSomeoneElse()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        var stranger = await _scenario.RegisterOwnerAsync("joao@exemplo.com", "111.444.777-35", "João Lima");
        var objectsBefore = await fixture.Bucket.CountAsync();

        // Act
        var response = await _scenario.SendPhotoAsync(stranger, conversationId, caption: null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await fixture.Bucket.CountAsync()).Should().Be(objectsBefore);
    }

    [Fact]
    public async Task SendPhoto_Should_Return400OnTheFileField_When_TheFileIsNotAnImage()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        using var form = new MultipartFormDataContent { { new ByteArrayContent("não é imagem"u8.ToArray()), "file", "nota.txt" } };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ConversationsRoute}/{conversationId}/messages/photo") { Content = form };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", pair.HostToken);

        // Act
        var response = await _client.SendAsync(request, Ct);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Equal("file");
    }
}
