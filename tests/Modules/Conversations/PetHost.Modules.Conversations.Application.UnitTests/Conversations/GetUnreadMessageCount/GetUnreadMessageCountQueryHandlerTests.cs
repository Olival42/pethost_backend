using FluentAssertions;
using Moq;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Application.Conversations.GetUnreadMessageCount;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Shared.Contracts.Authorization;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Conversations.GetUnreadMessageCount;

public sealed class GetUnreadMessageCountQueryHandlerTests
{
    private readonly Mock<IConversationParties> _parties = new(MockBehavior.Strict);
    private readonly Mock<IConversationQueries> _queries = new(MockBehavior.Strict);
    private readonly GetUnreadMessageCountQueryHandler _sut;

    public GetUnreadMessageCountQueryHandlerTests() =>
        _sut = new GetUnreadMessageCountQueryHandler(_parties.Object, _queries.Object);

    [Fact]
    public async Task HandleAsync_Should_ReturnTheTotalOfTheOwner_When_TheOwnerAsks()
    {
        // Arrange
        _parties.Setup(p => p.FindOwnerByAccountAsync(ConversationBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.AvailableOwner);
        _queries
            .Setup(q => q.CountUnreadAsync(new ConversationParticipant(ConversationParty.Owner, ConversationBuilder.OwnerId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);

        // Act
        var result = await _sut.HandleAsync(new GetUnreadMessageCountQuery(ConversationBuilder.OwnerUserId, Roles.Owner), CancellationToken.None);

        // Assert
        result.Value!.Total.Should().Be(4);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnZero_When_TheAccountHasNoHostProfileYet()
    {
        // Arrange
        _parties.Setup(p => p.FindHostByAccountAsync(ConversationBuilder.HostUserId, It.IsAny<CancellationToken>())).ReturnsAsync((PartyProfile?)null);

        // Act
        var result = await _sut.HandleAsync(new GetUnreadMessageCountQuery(ConversationBuilder.HostUserId, Roles.Host), CancellationToken.None);

        // Assert
        result.Value.Should().Be(new UnreadCountResponse(0));
        _queries.Verify(q => q.CountUnreadAsync(It.IsAny<ConversationParticipant>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
