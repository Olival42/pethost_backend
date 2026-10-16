using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations.PurgeInactiveConversations;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Conversations.PurgeInactiveConversations;

public sealed class PurgeInactiveConversationsCommandHandlerTests
{
    private const string PhotoUrl = "https://images.pethost.local/conversations/foto.webp";

    private static readonly DateTimeOffset Now = ConversationBuilder.DefaultNow.AddDays(200);
    private static readonly DateTimeOffset Cutoff = Now.AddDays(-90);

    private readonly Mock<IConversationRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IMessageRepository> _messages = new(MockBehavior.Strict);
    private readonly Mock<IConversationBookings> _bookings = new(MockBehavior.Strict);
    private readonly Mock<IImageStorage> _storage = new(MockBehavior.Strict);
    private readonly Mock<IAuditTrail> _auditTrail = new(MockBehavior.Strict);
    private readonly Conversation _inactive = new ConversationBuilder().Build();
    private readonly Conversation _withStay = new ConversationBuilder().WithOwner(TestIds.Of(70)).Build();
    private readonly PurgeInactiveConversationsCommandHandler _sut;

    public PurgeInactiveConversationsCommandHandlerTests()
    {
        _sut = new PurgeInactiveConversationsCommandHandler(
            _repository.Object,
            _messages.Object,
            _bookings.Object,
            _storage.Object,
            _auditTrail.Object,
            new FakeTimeProvider(Now));

        _bookings.Setup(b => b.HasOpenBookingAsync(_inactive.OwnerId, _inactive.HostId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _bookings.Setup(b => b.HasOpenBookingAsync(_withStay.OwnerId, _withStay.HostId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _messages.Setup(m => m.ListImageUrlsAsync(_inactive.Id, It.IsAny<CancellationToken>())).ReturnsAsync([PhotoUrl]);
        _storage.Setup(s => s.DeleteAsync(PhotoUrl, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _auditTrail.Setup(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task HandleAsync_Should_DeleteTheConversationItsPhotosAndRecordIt_When_ItIsInactive()
    {
        // Arrange
        GivenBatch(skip: 0, _inactive);
        GivenDeleted(_inactive, deleted: true);

        // Act
        var result = await _sut.HandleAsync(new PurgeInactiveConversationsCommand(InactivityDays: 90, BatchSize: 100), CancellationToken.None);

        // Assert
        result.Value!.Purged.Should().Be(1);
        _repository.Verify(r => r.DeleteIfInactiveAsync(_inactive.Id, Cutoff, It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync(PhotoUrl, It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.ConversationPurged && r.TargetId == _inactive.Id.Value && r.Details!["photos"] == "1"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_KeepTheConversation_When_ThePairHasAnOpenBooking()
    {
        // Arrange
        GivenBatch(skip: 0, _withStay);

        // Act
        var result = await _sut.HandleAsync(new PurgeInactiveConversationsCommand(InactivityDays: 90, BatchSize: 100), CancellationToken.None);

        // Assert
        result.Value!.Purged.Should().Be(0);
        _repository.Verify(r => r.DeleteIfInactiveAsync(It.IsAny<ConversationId>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_KeepThePhotos_When_AMessageArrivedBeforeTheDelete()
    {
        // Arrange
        GivenBatch(skip: 0, _inactive);
        GivenDeleted(_inactive, deleted: false);

        // Act
        var result = await _sut.HandleAsync(new PurgeInactiveConversationsCommand(InactivityDays: 90, BatchSize: 100), CancellationToken.None);

        // Assert
        result.Value!.Purged.Should().Be(0);
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_PageOverTheKeptConversations_When_ABatchIsFull()
    {
        // Arrange
        GivenBatch(skip: 0, _withStay);
        _repository
            .SetupSequence(r => r.ListInactiveAsync(Cutoff, 1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([_inactive])
            .ReturnsAsync([]);
        GivenDeleted(_inactive, deleted: true);

        // Act
        var result = await _sut.HandleAsync(new PurgeInactiveConversationsCommand(InactivityDays: 90, BatchSize: 1), CancellationToken.None);

        // Assert
        result.Value!.Purged.Should().Be(1);
        _repository.Verify(r => r.ListInactiveAsync(Cutoff, 1, 1, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private void GivenBatch(int skip, params Conversation[] conversations) =>
        _repository
            .Setup(r => r.ListInactiveAsync(Cutoff, skip, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(conversations);

    private void GivenDeleted(Conversation conversation, bool deleted) =>
        _repository
            .Setup(r => r.DeleteIfInactiveAsync(conversation.Id, Cutoff, It.IsAny<CancellationToken>()))
            .ReturnsAsync(deleted);
}
