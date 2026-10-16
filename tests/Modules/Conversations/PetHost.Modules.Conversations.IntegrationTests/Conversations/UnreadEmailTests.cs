using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Conversations.Application.Messages.NotifyUnreadMessages;
using PetHost.Shared.Kernel.Messaging;
using Xunit;
using static PetHost.Modules.Conversations.IntegrationTests.ConversationsScenario;

namespace PetHost.Modules.Conversations.IntegrationTests.Conversations;

/// <summary>
/// O e-mail de "mensagens não lidas" contra o banco e a caixa de saída de verdade. O tempo passando é
/// simulado com SQL; a tarefa em segundo plano fica desligada e o comando é chamado direto.
/// </summary>
[Collection(ConversationsApiCollection.Name)]
public sealed class UnreadEmailTests(ConversationsApiFixture fixture) : IAsyncLifetime
{
    private const string CidaEmail = "cida@exemplo.com";

    private HttpClient _client = null!;
    private ConversationsScenario _scenario = null!;

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

    [Fact]
    public async Task Notify_Should_EmailTheHostASummaryWithoutTheText_When_AMessageIsUnreadFor15Minutes()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "A Pipoca tem medo de fogos, tudo bem?");
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Posso levar no dia 20?");
        await AgeMessagesAsync(conversationId, minutes: 20);

        // Act
        var result = await NotifyAsync();

        // Assert
        result.EmailsSent.Should().Be(1);
        var email = await fixture.WaitForEmailAsync(e => e.ToAddress == CidaEmail && e.Subject.Contains("mensagens esperando", StringComparison.Ordinal), TimeSpan.FromSeconds(15));
        email.Should().NotBeNull();
        email!.TextBody.Should().Contain("2 mensagens de Camila Souza");
        email.TextBody.Should().NotContain("Pipoca", "o texto das mensagens fica no app");
    }

    [Fact]
    public async Task Notify_Should_WaitTheDelay_When_TheMessageIsRecent()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Oi!");
        await AgeMessagesAsync(conversationId, minutes: 10);

        // Act
        var result = await NotifyAsync();

        // Assert
        result.EmailsSent.Should().Be(0);
    }

    [Fact]
    public async Task Notify_Should_SendNothing_When_TheHostReadTheConversation()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Oi!");
        await AgeMessagesAsync(conversationId, minutes: 20);
        (await _scenario.SendAsync(HttpMethod.Put, $"{ConversationsRoute}/{conversationId}/read-marker", pair.HostToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Act
        var result = await NotifyAsync();

        // Assert
        result.EmailsSent.Should().Be(0);
    }

    [Fact]
    public async Task Notify_Should_NotRepeat_When_NothingNewArrivedOrTheCooldownHasNotPassed()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Oi!");
        await AgeMessagesAsync(conversationId, minutes: 20);
        (await NotifyAsync()).EmailsSent.Should().Be(1);

        // Act
        var nothingNew = await NotifyAsync();

        // Último e-mail há 30 min; mensagem nova, sem ler há 20 min: dentro do intervalo de 60.
        await AgeNotificationAsync(conversationId, minutes: 30);
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Dona Cida?");
        await AgeLatestMessageAsync(conversationId, minutes: 20);
        var insideCooldown = await NotifyAsync();

        await AgeNotificationAsync(conversationId, minutes: 61);
        var afterCooldown = await NotifyAsync();

        // Assert
        nothingNew.EmailsSent.Should().Be(0, "a mesma mensagem não gera um segundo e-mail");
        insideCooldown.EmailsSent.Should().Be(0, "menos de 60 minutos desde o último e-mail desta conversa");
        afterCooldown.EmailsSent.Should().Be(1, "mensagem nova, sem ler, depois do intervalo");
    }

    [Fact]
    public async Task Notify_Should_NotEmail_When_TheHostIsSuspended()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Oi!");
        await AgeMessagesAsync(conversationId, minutes: 20);
        await _scenario.SuspendTheHostAsync();

        // Act
        var result = await NotifyAsync();

        // Assert
        result.EmailsSent.Should().Be(0);
    }

    [Fact]
    public async Task NotificationSettings_Should_TurnTheEmailOffForThatAccountOnly_When_TheHostDisablesIt()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        const string Settings = $"{ConversationsRoute}/me/notification-settings";

        // Act
        var before = Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, Settings, pair.HostToken)));
        var turnedOff = await _scenario.SendAsync(HttpMethod.Put, Settings, pair.HostToken, new { unreadMessagesEmail = false });
        var after = Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, Settings, pair.HostToken)));
        var owner = Data(await ReadAsync(await _scenario.SendAsync(HttpMethod.Get, Settings, pair.OwnerToken)));

        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Oi!");
        await AgeMessagesAsync(conversationId, minutes: 20);
        var result = await NotifyAsync();

        // Assert
        before.GetProperty("unreadMessagesEmail").GetBoolean().Should().BeTrue("ligado por padrão");
        turnedOff.StatusCode.Should().Be(HttpStatusCode.OK);
        after.GetProperty("unreadMessagesEmail").GetBoolean().Should().BeFalse();
        owner.GetProperty("unreadMessagesEmail").GetBoolean().Should().BeTrue("a escolha é de cada conta");
        result.EmailsSent.Should().Be(0);
    }

    [Fact]
    public async Task NotificationSettings_Should_Return400_When_TheChoiceIsMissing()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();

        // Act
        var response = await _scenario.SendAsync(HttpMethod.Put, $"{ConversationsRoute}/me/notification-settings", pair.OwnerToken, new { });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Fields(await ReadAsync(response)).Should().Contain("unreadMessagesEmail");
    }

    /// <summary>Faz as mensagens da conversa parecerem enviadas há <paramref name="minutes"/> minutos.</summary>
    private Task AgeMessagesAsync(Guid conversationId, int minutes) =>
        fixture.ExecuteAsync(
            $"UPDATE conversation.messages SET created_at = now() - interval '{minutes} minutes' WHERE conversation_id = '{conversationId}'");

    /// <summary>Faz só a mensagem mais nova da conversa parecer enviada há <paramref name="minutes"/> minutos.</summary>
    private Task AgeLatestMessageAsync(Guid conversationId, int minutes) =>
        fixture.ExecuteAsync(
            $"UPDATE conversation.messages SET created_at = now() - interval '{minutes} minutes' WHERE id = " +
            $"(SELECT id FROM conversation.messages WHERE conversation_id = '{conversationId}' ORDER BY created_at DESC, id DESC LIMIT 1)");

    /// <summary>Faz o último e-mail da anfitriã nesta conversa parecer de <paramref name="minutes"/> minutos atrás.</summary>
    private Task AgeNotificationAsync(Guid conversationId, int minutes) =>
        fixture.ExecuteAsync(
            $"UPDATE conversation.conversations SET host_unread_notified_at = now() - interval '{minutes} minutes' WHERE id = '{conversationId}'");

    private async Task<NotifyUnreadMessagesResponse> NotifyAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<NotifyUnreadMessagesCommand, NotifyUnreadMessagesResponse>>();

        var result = await handler.HandleAsync(new NotifyUnreadMessagesCommand(DelayMinutes: 15, CooldownMinutes: 60, BatchSize: 100), TestContext.Current.CancellationToken);

        return result.Value!;
    }
}
