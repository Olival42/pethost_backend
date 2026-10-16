using FluentAssertions;
using Moq;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations.GetConversationById;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Contracts.Authorization;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Conversations.GetConversationById;

public sealed class GetConversationByIdQueryHandlerTests
{
    private readonly Mock<IConversationRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IConversationParties> _parties = new(MockBehavior.Strict);
    private readonly Mock<IConversationQueries> _queries = new(MockBehavior.Strict);
    private readonly Conversation _conversation = new ConversationBuilder().Build();
    private readonly GetConversationByIdQueryHandler _sut;

    public GetConversationByIdQueryHandlerTests()
    {
        _sut = new GetConversationByIdQueryHandler(_repository.Object, _parties.Object, _queries.Object, new ConversationResponseBuilder(_parties.Object));

        _parties.Setup(p => p.FindHostByAccountAsync(ConversationBuilder.HostUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.AvailableHost);
        _parties.Setup(p => p.GetOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Owners());
        _parties.Setup(p => p.GetHostsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Hosts());
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnTheConversationWithTheUnreadOfTheViewer_When_TheHostAsks()
    {
        // Arrange
        _repository.Setup(r => r.GetByIdAsync(_conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_conversation);
        _queries.Setup(q => q.GetAsync(_conversation.Id, ConversationParty.Host, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Row(_conversation, unread: 3));

        // Act
        var result = await _sut.HandleAsync(new GetConversationByIdQuery(ConversationBuilder.HostUserId, Roles.Host, _conversation.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.Id.Should().Be(_conversation.Id.Value);
        result.Value.UnreadCount.Should().Be(3);
        result.Value.Host!.Name.Should().Be("Dona Cida");
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_TheConversationIsFromAnotherHost()
    {
        // Arrange
        var otherHost = new ConversationBuilder().WithHost(TestIds.Of(88)).Build();
        _repository.Setup(r => r.GetByIdAsync(otherHost.Id, It.IsAny<CancellationToken>())).ReturnsAsync(otherHost);

        // Act
        var result = await _sut.HandleAsync(new GetConversationByIdQuery(ConversationBuilder.HostUserId, Roles.Host, otherHost.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.NotFound(otherHost.Id));
        _queries.Verify(q => q.GetAsync(It.IsAny<ConversationId>(), It.IsAny<ConversationParty>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_TheConversationDisappearsBeforeTheRead()
    {
        // Arrange
        _repository.Setup(r => r.GetByIdAsync(_conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_conversation);
        _queries.Setup(q => q.GetAsync(_conversation.Id, ConversationParty.Host, It.IsAny<CancellationToken>())).ReturnsAsync((ConversationRow?)null);

        // Act
        var result = await _sut.HandleAsync(new GetConversationByIdQuery(ConversationBuilder.HostUserId, Roles.Host, _conversation.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.NotFound(_conversation.Id));
    }
}
