using FluentAssertions;
using Moq;
using PetHost.Modules.Conversations.Application.Conversations.EraseConversationsOfParty;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Conversations.EraseConversationsOfParty;

public sealed class EraseConversationsOfPartyCommandHandlerTests
{
    private const string PhotoUrl = "https://images.pethost.local/conversations/foto.webp";

    private readonly Mock<IConversationRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IMessageRepository> _messages = new(MockBehavior.Strict);
    private readonly Mock<IImageStorage> _storage = new(MockBehavior.Strict);
    private readonly Mock<IPrivateImageStorage> _privateStorage = new(MockBehavior.Strict);
    private readonly Mock<IAuditTrail> _auditTrail = new(MockBehavior.Strict);
    private readonly Conversation _withPhoto = new ConversationBuilder().Build();
    private readonly Conversation _withoutPhoto = new ConversationBuilder().WithHost(TestIds.Of(71)).Build();
    private readonly List<string> _calls = [];
    private readonly EraseConversationsOfPartyCommandHandler _sut;

    public EraseConversationsOfPartyCommandHandlerTests()
    {
        _sut = new EraseConversationsOfPartyCommandHandler(_repository.Object, _messages.Object, _storage.Object, _privateStorage.Object, _auditTrail.Object);

        _repository
            .Setup(r => r.ListByPartyAsync(ConversationParty.Owner, ConversationBuilder.OwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_withPhoto, _withoutPhoto]);
        _repository
            .Setup(r => r.DeleteAsync(It.IsAny<ConversationId>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("delete"))
            .ReturnsAsync(true);
        _messages.Setup(m => m.ListImageUrlsAsync(_withPhoto.Id, It.IsAny<CancellationToken>())).ReturnsAsync([PhotoUrl]);
        _messages.Setup(m => m.ListImageUrlsAsync(_withoutPhoto.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _messages.Setup(m => m.ListImageKeysAsync(It.IsAny<ConversationId>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _storage
            .Setup(s => s.DeleteAsync(PhotoUrl, It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("photo"))
            .Returns(Task.CompletedTask);
        _auditTrail.Setup(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task HandleAsync_Should_DeleteEveryConversationOfTheParty_When_Called()
    {
        // Act
        var result = await _sut.HandleAsync(
            new EraseConversationsOfPartyCommand(ConversationParty.Owner, ConversationBuilder.OwnerId),
            CancellationToken.None);

        // Assert
        result.Value.Should().Be(2);
        _repository.Verify(r => r.DeleteAsync(_withPhoto.Id, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.DeleteAsync(_withoutPhoto.Id, It.IsAny<CancellationToken>()), Times.Once);
        _calls.Should().Equal(["delete", "photo", "delete"], "a foto sai do bucket depois que a conversa saiu do banco");
        _auditTrail.Verify(
            a => a.RecordAsync(
                It.Is<AuditRecord>(r => r.Action == AuditActions.ConversationPurged && r.Details!["cause"] == "account_erased"),
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task HandleAsync_Should_KeepThePhotos_When_TheConversationWasAlreadyGone()
    {
        // Arrange
        _repository.Setup(r => r.DeleteAsync(_withPhoto.Id, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        // Act
        var result = await _sut.HandleAsync(
            new EraseConversationsOfPartyCommand(ConversationParty.Owner, ConversationBuilder.OwnerId),
            CancellationToken.None);

        // Assert
        result.Value.Should().Be(1);
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnZero_When_PartyHasNoConversation()
    {
        // Arrange
        _repository
            .Setup(r => r.ListByPartyAsync(ConversationParty.Host, ConversationBuilder.HostId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await _sut.HandleAsync(
            new EraseConversationsOfPartyCommand(ConversationParty.Host, ConversationBuilder.HostId),
            CancellationToken.None);

        // Assert
        result.Value.Should().Be(0);
    }

    [Fact]
    public void Validator_Should_RequireThePartyId_When_Empty()
    {
        // Act
        var result = new EraseConversationsOfPartyCommandValidator()
            .Validate(new EraseConversationsOfPartyCommand(ConversationParty.Owner, Guid.Empty));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "PartyId");
    }
}
