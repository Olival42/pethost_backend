using FluentAssertions;
using Moq;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Conversations.Responses;

public sealed class ConversationResponseBuilderTests
{
    private readonly Mock<IConversationParties> _parties = new(MockBehavior.Strict);
    private readonly Conversation _conversation = new ConversationBuilder().Build();
    private readonly ConversationResponseBuilder _sut;

    public ConversationResponseBuilderTests() => _sut = new ConversationResponseBuilder(_parties.Object, ConversationsTestData.PhotoLinks());

    [Fact]
    public async Task BuildAsync_Should_JoinTheNamesTheLastMessageAndTheUnreadCount_When_TheRowHasThem()
    {
        // Arrange
        GivenParties(ConversationsTestData.Owners(), ConversationsTestData.Hosts());
        var last = ConversationsTestData.MessageRow(_conversation, ConversationParty.Host, "Pode trazer!", ConversationBuilder.DefaultNow.AddMinutes(4));

        // Act
        var response = await _sut.BuildAsync(ConversationsTestData.Row(_conversation, unread: 1, last), CancellationToken.None);

        // Assert
        response.Id.Should().Be(_conversation.Id.Value);
        response.Owner.Should().Be(new PartySummary(ConversationBuilder.OwnerId, "Camila Souza", "https://images.pethost.local/avatars/camila.webp"));
        response.Host.Should().Be(new PartySummary(ConversationBuilder.HostId, "Dona Cida", null));
        response.LastMessage!.Body.Should().Be("Pode trazer!");
        response.UnreadCount.Should().Be(1);
        response.CreatedAt.Should().Be(ConversationBuilder.DefaultNow);
    }

    [Fact]
    public async Task BuildAsync_Should_LeaveThePartyOut_When_TheAccountIsGone()
    {
        // Arrange
        GivenParties(ConversationsTestData.Owners(), ConversationsTestData.Nobody());

        // Act
        var response = await _sut.BuildAsync(ConversationsTestData.Row(_conversation), CancellationToken.None);

        // Assert
        response.Host.Should().BeNull();
        response.HostId.Should().Be(ConversationBuilder.HostId);
        response.LastMessage.Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_Should_ReadThePartiesOnceForThePage_When_ThereAreManyRows()
    {
        // Arrange
        GivenParties(ConversationsTestData.Owners(), ConversationsTestData.Hosts());
        var another = new ConversationBuilder().WithOwner(TestIds.Of(70)).Build();

        // Act
        var responses = await _sut.BuildAsync([ConversationsTestData.Row(_conversation), ConversationsTestData.Row(another)], CancellationToken.None);

        // Assert
        responses.Should().HaveCount(2);
        _parties.Verify(p => p.GetOwnersAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2), It.IsAny<CancellationToken>()), Times.Once);
        _parties.Verify(p => p.GetHostsAsync(It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BuildAsync_Should_ReturnEmptyWithoutReadingTheParties_When_ThereAreNoRows()
    {
        // Act
        var responses = await _sut.BuildAsync([], CancellationToken.None);

        // Assert
        responses.Should().BeEmpty();
    }

    private void GivenParties(IReadOnlyDictionary<Guid, PartyInfo> owners, IReadOnlyDictionary<Guid, PartyInfo> hosts)
    {
        _parties.Setup(p => p.GetOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(owners);
        _parties.Setup(p => p.GetHostsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(hosts);
    }
}
