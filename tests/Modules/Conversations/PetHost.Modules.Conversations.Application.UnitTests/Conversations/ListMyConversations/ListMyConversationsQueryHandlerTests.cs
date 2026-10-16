using FluentAssertions;
using Moq;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Application.Conversations.ListMyConversations;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Contracts.Authorization;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Conversations.ListMyConversations;

public sealed class ListMyConversationsQueryHandlerTests
{
    private readonly Mock<IConversationParties> _parties = new(MockBehavior.Strict);
    private readonly Mock<IConversationQueries> _queries = new(MockBehavior.Strict);
    private readonly Conversation _conversation = new ConversationBuilder().Build();
    private readonly ListMyConversationsQueryHandler _sut;

    public ListMyConversationsQueryHandlerTests()
    {
        _sut = new ListMyConversationsQueryHandler(_parties.Object, _queries.Object, new ConversationResponseBuilder(_parties.Object));

        _parties.Setup(p => p.GetOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Owners());
        _parties.Setup(p => p.GetHostsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Hosts());
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnThePageWithThePartiesAndTheUnreadCount_When_TheOwnerAsks()
    {
        // Arrange
        var lastMessage = ConversationsTestData.MessageRow(_conversation, ConversationParty.Host, "Pode trazer a Pipoca!", ConversationBuilder.DefaultNow);
        _parties.Setup(p => p.FindOwnerByAccountAsync(ConversationBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.AvailableOwner);
        GivenList(new ConversationParticipant(ConversationParty.Owner, ConversationBuilder.OwnerId), [ConversationsTestData.Row(_conversation, unread: 2, lastMessage)], total: 21);

        // Act
        var result = await _sut.HandleAsync(new ListMyConversationsQuery(ConversationBuilder.OwnerUserId, Roles.Owner, Page: 2, PageSize: 20), CancellationToken.None);

        // Assert
        var page = result.Value!;
        page.Page.Should().Be(2);
        page.TotalCount.Should().Be(21);
        page.TotalPages.Should().Be(2);
        var item = page.Items.Should().ContainSingle().Subject;
        item.Owner!.Name.Should().Be("Camila Souza");
        item.Host!.Name.Should().Be("Dona Cida");
        item.UnreadCount.Should().Be(2);
        item.LastMessage!.Body.Should().Be("Pode trazer a Pipoca!");
        item.LastMessage.SentBy.Should().Be("host");
    }

    [Fact]
    public async Task HandleAsync_Should_ListAsTheHost_When_TheRoleIsHost()
    {
        // Arrange
        _parties.Setup(p => p.FindHostByAccountAsync(ConversationBuilder.HostUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.AvailableHost);
        GivenList(new ConversationParticipant(ConversationParty.Host, ConversationBuilder.HostId), [ConversationsTestData.Row(_conversation)], total: 1);

        // Act
        var result = await _sut.HandleAsync(new ListMyConversationsQuery(ConversationBuilder.HostUserId, Roles.Host), CancellationToken.None);

        // Assert
        result.Value!.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task HandleAsync_Should_StillListTheHistory_When_TheOwnerIsSuspended()
    {
        // Arrange
        _parties.Setup(p => p.FindOwnerByAccountAsync(ConversationBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.SuspendedOwner);
        GivenList(new ConversationParticipant(ConversationParty.Owner, ConversationBuilder.OwnerId), [ConversationsTestData.Row(_conversation)], total: 1);

        // Act
        var result = await _sut.HandleAsync(new ListMyConversationsQuery(ConversationBuilder.OwnerUserId, Roles.Owner), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnAnEmptyPageWithoutReadingTheParties_When_ThereAreNoConversations()
    {
        // Arrange
        _parties.Setup(p => p.FindOwnerByAccountAsync(ConversationBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.AvailableOwner);
        GivenList(new ConversationParticipant(ConversationParty.Owner, ConversationBuilder.OwnerId), [], total: 0);

        // Act
        var result = await _sut.HandleAsync(new ListMyConversationsQuery(ConversationBuilder.OwnerUserId, Roles.Owner), CancellationToken.None);

        // Assert
        result.Value!.Items.Should().BeEmpty();
        _parties.Verify(p => p.GetOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnBothPagingErrors_When_PageAndPageSizeAreInvalid()
    {
        // Act
        var result = await _sut.HandleAsync(new ListMyConversationsQuery(ConversationBuilder.OwnerUserId, Roles.Owner, Page: 0, PageSize: 101), CancellationToken.None);

        // Assert
        result.Errors.Should().BeEquivalentTo([ConversationsErrors.PageInvalid, ConversationsErrors.PageSizeInvalid]);
        _parties.Verify(p => p.FindOwnerByAccountAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnAnEmptyPage_When_TheAccountHasNoHostProfileYet()
    {
        // Arrange
        _parties.Setup(p => p.FindHostByAccountAsync(ConversationBuilder.HostUserId, It.IsAny<CancellationToken>())).ReturnsAsync((PartyProfile?)null);

        // Act
        var result = await _sut.HandleAsync(new ListMyConversationsQuery(ConversationBuilder.HostUserId, Roles.Host, Page: 2, PageSize: 10), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Items.Should().BeEmpty();
        result.Value.Page.Should().Be(2);
        result.Value.PageSize.Should().Be(10);
        result.Value.TotalCount.Should().Be(0);
        _queries.Verify(q => q.ListAsync(It.IsAny<ConversationParticipant>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_StillValidateThePage_When_TheAccountHasNoHostProfileYet()
    {
        // Act
        var result = await _sut.HandleAsync(new ListMyConversationsQuery(ConversationBuilder.HostUserId, Roles.Host, Page: 0), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.PageInvalid);
    }

    private void GivenList(ConversationParticipant participant, IReadOnlyList<ConversationRow> rows, long total) =>
        _queries
            .Setup(q => q.ListAsync(participant, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((rows, total));
}
