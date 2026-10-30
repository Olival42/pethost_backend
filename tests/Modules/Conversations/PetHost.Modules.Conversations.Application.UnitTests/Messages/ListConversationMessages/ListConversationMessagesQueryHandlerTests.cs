using FluentAssertions;
using Moq;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Messages.ListConversationMessages;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Contracts.Authorization;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Messages.ListConversationMessages;

public sealed class ListConversationMessagesQueryHandlerTests
{
    private readonly Mock<IConversationRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IConversationParties> _parties = new(MockBehavior.Strict);
    private readonly Mock<IConversationQueries> _queries = new(MockBehavior.Strict);
    private readonly Conversation _conversation = new ConversationBuilder().Build();
    private readonly ListConversationMessagesQueryHandler _sut;

    public ListConversationMessagesQueryHandlerTests()
    {
        _sut = new ListConversationMessagesQueryHandler(_repository.Object, _parties.Object, _queries.Object, ConversationsTestData.PhotoLinks());

        _parties.Setup(p => p.FindOwnerByAccountAsync(ConversationBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.AvailableOwner);
        _repository.Setup(r => r.GetByIdAsync(_conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_conversation);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnThePageAndSayThereIsMore_When_TheReadBringsOneMoreThanThePage()
    {
        // Arrange
        GivenMessages(before: null, take: 3, Row("terceira", 3), Row("segunda", 2), Row("primeira", 1));

        // Act
        var result = await _sut.HandleAsync(Query(before: null, pageSize: 2), CancellationToken.None);

        // Assert
        var page = result.Value!;
        page.Items.Select(m => m.Body).Should().Equal("terceira", "segunda");
        page.PageSize.Should().Be(2);
        page.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_Should_SayThereIsNoMore_When_TheReadFitsInThePage()
    {
        // Arrange
        GivenMessages(before: null, take: 3, Row("única", 1));

        // Act
        var result = await _sut.HandleAsync(Query(before: null, pageSize: 2), CancellationToken.None);

        // Assert
        result.Value!.Items.Should().ContainSingle();
        result.Value.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_Should_ReadBeforeTheCursor_When_BeforeIsAMessageId()
    {
        // Arrange
        var cursor = TestIds.Of(5);
        GivenMessages(new MessageId(cursor), take: 51, Row("antiga", 1));

        // Act
        var result = await _sut.HandleAsync(Query(before: $" {cursor} ", pageSize: ListConversationMessagesQuery.DefaultPageSize), CancellationToken.None);

        // Assert
        result.Value!.Items.Should().ContainSingle(m => m.Body == "antiga");
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnBeforeInvalid_When_TheCursorIsNotAMessageOfThisConversation()
    {
        // Arrange
        var cursor = TestIds.Of(5);
        _queries
            .Setup(q => q.ListMessagesAsync(_conversation.Id, new MessageId(cursor), 51, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<MessageRow>?)null);

        // Act
        var result = await _sut.HandleAsync(Query(before: cursor.ToString(), pageSize: 50), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.BeforeInvalid);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnBeforeInvalidWithoutReading_When_TheCursorIsNotAnId()
    {
        // Act
        var result = await _sut.HandleAsync(Query(before: "ontem", pageSize: 50), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.BeforeInvalid);
        _repository.Verify(r => r.GetByIdAsync(It.IsAny<ConversationId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task HandleAsync_Should_ReturnPageSizeInvalid_When_ThePageSizeIsOutOfRange(int pageSize)
    {
        // Act
        var result = await _sut.HandleAsync(Query(before: null, pageSize), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.PageSizeInvalid);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFoundWithoutReading_When_TheConversationIsFromAnotherOwner()
    {
        // Arrange
        var someoneElses = new ConversationBuilder().WithOwner(TestIds.Of(77)).Build();
        _repository.Setup(r => r.GetByIdAsync(someoneElses.Id, It.IsAny<CancellationToken>())).ReturnsAsync(someoneElses);

        // Act
        var result = await _sut.HandleAsync(
            new ListConversationMessagesQuery(ConversationBuilder.OwnerUserId, Roles.Owner, someoneElses.Id.Value),
            CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.NotFound(someoneElses.Id));
        _queries.Verify(q => q.ListMessagesAsync(It.IsAny<ConversationId>(), It.IsAny<MessageId?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private ListConversationMessagesQuery Query(string? before, int pageSize) =>
        new(ConversationBuilder.OwnerUserId, Roles.Owner, _conversation.Id.Value, before, pageSize);

    private MessageRow Row(string body, int minutes) =>
        ConversationsTestData.MessageRow(_conversation, ConversationParty.Host, body, ConversationBuilder.DefaultNow.AddMinutes(minutes));

    private void GivenMessages(MessageId? before, int take, params MessageRow[] rows) =>
        _queries
            .Setup(q => q.ListMessagesAsync(_conversation.Id, before, take, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
}
