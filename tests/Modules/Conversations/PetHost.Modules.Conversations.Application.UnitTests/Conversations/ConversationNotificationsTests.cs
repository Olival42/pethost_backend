using FluentAssertions;
using Moq;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Application.Messages.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.TestKit;
using PetHost.Shared.Contracts.Storage;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Conversations;

public sealed class ConversationNotificationsTests
{
    private readonly Mock<IConversationParties> _parties = new(MockBehavior.Strict);
    private readonly Mock<IConversationQueries> _queries = new(MockBehavior.Strict);
    private readonly Mock<IConversationRealtime> _realtime = new(MockBehavior.Strict);
    private readonly Conversation _conversation = new ConversationBuilder().Build();
    private readonly MessageResponse _message;
    private readonly ConversationNotifications _sut;

    public ConversationNotificationsTests()
    {
        _sut = new ConversationNotifications(_parties.Object, _queries.Object, _realtime.Object);
        _message = MessageResponse.From(ConversationBuilder.Text(_conversation, ConversationParty.Owner));

        _queries.Setup(q => q.CountUnreadAsync(It.IsAny<ConversationParticipant>(), It.IsAny<CancellationToken>())).ReturnsAsync(2);
        _realtime.Setup(r => r.MessageCreatedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), _message, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _realtime.Setup(r => r.UnreadCountChangedAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task MessageSentAsync_Should_PushTheMessageToBothAccountsAndTheCountToTheHost_When_TheOwnerSent()
    {
        // Arrange
        GivenParties(ConversationsTestData.Owners(), ConversationsTestData.Hosts());

        // Act
        await _sut.MessageSentAsync(_conversation, ConversationParty.Owner, _message, CancellationToken.None);

        // Assert
        _realtime.Verify(r => r.MessageCreatedAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(ConversationBuilder.OwnerUserId) && ids.Contains(ConversationBuilder.HostUserId)),
            _message,
            It.IsAny<CancellationToken>()), Times.Once);
        _queries.Verify(q => q.CountUnreadAsync(new ConversationParticipant(ConversationParty.Host, ConversationBuilder.HostId), It.IsAny<CancellationToken>()), Times.Once);
        _realtime.Verify(r => r.UnreadCountChangedAsync(ConversationBuilder.HostUserId, 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MessageSentAsync_Should_PushTheCountToTheOwner_When_TheHostSent()
    {
        // Arrange
        GivenParties(ConversationsTestData.Owners(), ConversationsTestData.Hosts());

        // Act
        await _sut.MessageSentAsync(_conversation, ConversationParty.Host, _message, CancellationToken.None);

        // Assert
        _realtime.Verify(r => r.UnreadCountChangedAsync(ConversationBuilder.OwnerUserId, 2, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MessageSentAsync_Should_SkipTheCount_When_TheRecipientAccountIsGone()
    {
        // Arrange
        GivenParties(ConversationsTestData.Owners(), ConversationsTestData.Nobody());

        // Act
        await _sut.MessageSentAsync(_conversation, ConversationParty.Owner, _message, CancellationToken.None);

        // Assert
        _realtime.Verify(r => r.MessageCreatedAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(ConversationBuilder.OwnerUserId)),
            _message,
            It.IsAny<CancellationToken>()), Times.Once);
        _realtime.Verify(r => r.UnreadCountChangedAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MessageSentAsync_Should_PushNothing_When_BothAccountsAreGone()
    {
        // Arrange
        GivenParties(ConversationsTestData.Nobody(), ConversationsTestData.Nobody());

        // Act
        await _sut.MessageSentAsync(_conversation, ConversationParty.Owner, _message, CancellationToken.None);

        // Assert
        _realtime.Verify(r => r.MessageCreatedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<MessageResponse>(), It.IsAny<CancellationToken>()), Times.Never);
        _realtime.Verify(r => r.UnreadCountChangedAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MessageSentAsync_Should_WaitForCommit_When_AConfirmationTransactionIsActive()
    {
        GivenParties(ConversationsTestData.Owners(), ConversationsTestData.Hosts());
        var effects = new Mock<ICommitEffects>(MockBehavior.Strict);
        Func<CancellationToken, Task>? delivery = null;
        effects.SetupGet(e => e.IsActive).Returns(true);
        effects.Setup(e => e.AfterCommit(It.IsAny<Func<CancellationToken, Task>>()))
            .Callback<Func<CancellationToken, Task>>(action => delivery = action);
        var sut = new ConversationNotifications(_parties.Object, _queries.Object, _realtime.Object, effects.Object);
        await sut.MessageSentAsync(_conversation, ConversationParty.Owner, _message, CancellationToken.None);
        _realtime.Verify(r => r.MessageCreatedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<MessageResponse>(), It.IsAny<CancellationToken>()), Times.Never);
        delivery.Should().NotBeNull();
        effects.SetupGet(e => e.IsActive).Returns(false);
        await delivery!(CancellationToken.None);
        _realtime.Verify(r => r.MessageCreatedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), _message, It.IsAny<CancellationToken>()), Times.Once);
    }

    private void GivenParties(IReadOnlyDictionary<Guid, PartyInfo> owners, IReadOnlyDictionary<Guid, PartyInfo> hosts)
    {
        _parties.Setup(p => p.GetOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(owners);
        _parties.Setup(p => p.GetHostsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(hosts);
    }
}
