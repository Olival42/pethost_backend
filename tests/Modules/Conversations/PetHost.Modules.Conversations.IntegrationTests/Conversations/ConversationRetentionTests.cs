using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PetHost.Modules.Conversations.Application.Conversations.PurgeInactiveConversations;
using PetHost.Shared.Kernel.Messaging;
using Xunit;
using static PetHost.Modules.Conversations.IntegrationTests.ConversationsScenario;

namespace PetHost.Modules.Conversations.IntegrationTests.Conversations;

/// <summary>
/// A retenção (LGPD) contra o banco e o bucket de verdade: conversa parada há mais de 90 dias some
/// com as mensagens e as fotos, a não ser que a dupla tenha reserva em aberto. O tempo passando é
/// simulado com SQL; a tarefa em segundo plano fica desligada e o comando é chamado direto.
/// </summary>
[Collection(ConversationsApiCollection.Name)]
public sealed class ConversationRetentionTests(ConversationsApiFixture fixture) : IAsyncLifetime
{
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
    public async Task Purge_Should_DeleteTheConversationItsMessagesAndItsPhotos_When_ItIsInactiveForMoreThan90Days()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        var photo = Data(await ReadAsync(await _scenario.SendPhotoAsync(pair.HostToken, conversationId, caption: null))).GetProperty("imageUrl").GetString()!;
        await AgeAsync(conversationId, days: 100);

        // Act
        var result = await PurgeAsync();

        // Assert
        result.Purged.Should().Be(1);
        (await _scenario.SendAsync(HttpMethod.Get, $"{ConversationsRoute}/{conversationId}", pair.OwnerToken)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await fixture.Bucket.ExistsAsync(photo)).Should().BeFalse();
    }

    [Fact]
    public async Task Purge_Should_KeepTheConversation_When_ThePairHasAnOpenBooking()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Combinado para dezembro!");
        await _scenario.RequestStayAsync(pair);
        await AgeAsync(conversationId, days: 100);

        // Act
        var result = await PurgeAsync();

        // Assert
        result.Purged.Should().Be(0);
        (await _scenario.SendAsync(HttpMethod.Get, $"{ConversationsRoute}/{conversationId}", pair.OwnerToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Purge_Should_KeepTheConversation_When_ThereWasActivityInsideThePeriod()
    {
        // Arrange
        var pair = await _scenario.ArrangeAsync();
        var conversationId = await _scenario.StartAsync(pair);
        await _scenario.SendTextAsync(pair.OwnerToken, conversationId, "Oi!");
        await AgeAsync(conversationId, days: 89);

        // Act
        var result = await PurgeAsync();

        // Assert
        result.Purged.Should().Be(0);
    }

    /// <summary>Faz a conversa parecer parada há <paramref name="days"/> dias.</summary>
    private Task AgeAsync(Guid conversationId, int days) =>
        fixture.ExecuteAsync(
            $"UPDATE conversation.conversations SET last_message_at = now() - interval '{days} days', created_at = now() - interval '{days + 10} days' WHERE id = '{conversationId}'");

    private async Task<PurgeInactiveConversationsResponse> PurgeAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<PurgeInactiveConversationsCommand, PurgeInactiveConversationsResponse>>();

        var result = await handler.HandleAsync(new PurgeInactiveConversationsCommand(InactivityDays: 90, BatchSize: 100), TestContext.Current.CancellationToken);

        return result.Value!;
    }
}
