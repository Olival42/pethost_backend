using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Application.Conversations.MarkConversationRead;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Contracts.Authorization;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Conversations.MarkConversationRead;

public sealed class MarkConversationReadCommandHandlerTests
{
    private static readonly DateTimeOffset Now = ConversationBuilder.DefaultNow.AddHours(1);
    private static readonly ConversationParticipant Owner = new(ConversationParty.Owner, ConversationBuilder.OwnerId);

    private readonly Mock<IConversationRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IMessageRepository> _messages = new(MockBehavior.Strict);
    private readonly Mock<IConversationParties> _parties = new(MockBehavior.Strict);
    private readonly Mock<IConversationQueries> _queries = new(MockBehavior.Strict);
    private readonly Mock<IConversationRealtime> _realtime = new(MockBehavior.Strict);
    private readonly Conversation _conversation = new ConversationBuilder().Build();
    private readonly MarkConversationReadCommandHandler _sut;

    public MarkConversationReadCommandHandlerTests()
    {
        _sut = new MarkConversationReadCommandHandler(
            _repository.Object,
            _messages.Object,
            _parties.Object,
            _queries.Object,
            _realtime.Object,
            new FakeTimeProvider(Now));

        _parties.Setup(p => p.FindOwnerByAccountAsync(ConversationBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.AvailableOwner);
        _repository.Setup(r => r.GetByIdAsync(_conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_conversation);
        _queries.Setup(q => q.CountUnreadAsync(Owner, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _realtime.Setup(r => r.UnreadCountChangedAsync(ConversationBuilder.OwnerUserId, It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    private MarkConversationReadCommand Command => new(ConversationBuilder.OwnerUserId, Roles.Owner, _conversation.Id.Value);

    [Fact]
    public async Task HandleAsync_Should_MarkTheReceivedMessagesAndPushTheNewTotal_When_SomethingWasUnread()
    {
        // Arrange
        GivenMarked(3);

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.Value!.Total.Should().Be(1, "o tutor ainda tem uma não lida em outra conversa");
        _messages.Verify(m => m.MarkReceivedAsReadAsync(_conversation.Id, ConversationParty.Owner, Now, It.IsAny<CancellationToken>()), Times.Once);
        _realtime.Verify(r => r.UnreadCountChangedAsync(ConversationBuilder.OwnerUserId, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_NotPush_When_NothingWasUnread()
    {
        // Arrange
        GivenMarked(0);

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _realtime.Verify(r => r.UnreadCountChangedAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_StillMark_When_TheOwnerIsSuspended()
    {
        // Arrange
        _parties.Setup(p => p.FindOwnerByAccountAsync(ConversationBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.SuspendedOwner);
        GivenMarked(1);

        // Act
        var result = await _sut.HandleAsync(Command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFoundWithoutMarking_When_TheConversationIsFromAnotherOwner()
    {
        // Arrange
        var someoneElses = new ConversationBuilder().WithOwner(TestIds.Of(77)).Build();
        _repository.Setup(r => r.GetByIdAsync(someoneElses.Id, It.IsAny<CancellationToken>())).ReturnsAsync(someoneElses);

        // Act
        var result = await _sut.HandleAsync(new MarkConversationReadCommand(ConversationBuilder.OwnerUserId, Roles.Owner, someoneElses.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.NotFound(someoneElses.Id));
        _messages.Verify(m => m.MarkReceivedAsReadAsync(It.IsAny<ConversationId>(), It.IsAny<ConversationParty>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private void GivenMarked(int marked) =>
        _messages
            .Setup(m => m.MarkReceivedAsReadAsync(_conversation.Id, ConversationParty.Owner, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(marked);
}
