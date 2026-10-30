using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Application.Conversations.UnblockConversation;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Conversations.UnblockConversation;

public sealed class UnblockConversationCommandHandlerTests
{
    private static readonly DateTimeOffset Now = ConversationBuilder.DefaultNow.AddHours(5);

    private readonly Mock<IConversationRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IConversationsUnitOfWork> _unitOfWork = new(MockBehavior.Strict);
    private readonly Mock<IConversationParties> _parties = new(MockBehavior.Strict);
    private readonly Mock<IConversationQueries> _queries = new(MockBehavior.Strict);
    private readonly Mock<IConversationRealtime> _realtime = new(MockBehavior.Strict);
    private readonly Mock<IAuditTrail> _auditTrail = new(MockBehavior.Strict);
    private readonly Conversation _conversation = new ConversationBuilder().Build();
    private readonly UnblockConversationCommandHandler _sut;

    public UnblockConversationCommandHandlerTests()
    {
        _sut = new UnblockConversationCommandHandler(
            _repository.Object,
            _unitOfWork.Object,
            _parties.Object,
            _queries.Object,
            new ConversationResponseBuilder(_parties.Object, ConversationsTestData.PhotoLinks()),
            new ConversationNotifications(_parties.Object, _queries.Object, _realtime.Object),
            _auditTrail.Object,
            new FakeTimeProvider(Now));

        _parties.Setup(p => p.FindOwnerByAccountAsync(ConversationBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.AvailableOwner);
        _parties.Setup(p => p.GetOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Owners());
        _parties.Setup(p => p.GetHostsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Hosts());
        _repository.Setup(r => r.GetByIdAsync(_conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_conversation);
        _repository.Setup(r => r.ReloadAsync(_conversation, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _unitOfWork
            .Setup(u => u.RunLockedOnConversationAsync(It.IsAny<ConversationId>(), It.IsAny<Func<CancellationToken, Task<Result<bool>>>>(), It.IsAny<CancellationToken>()))
            .Returns<ConversationId, Func<CancellationToken, Task<Result<bool>>>, CancellationToken>((_, work, ct) => work(ct));
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        _auditTrail.Setup(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _realtime.Setup(r => r.BlockChangedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<ConversationBlockResponse>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _queries
            .Setup(q => q.GetAsync(_conversation.Id, ConversationParty.Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ConversationsTestData.Row(_conversation) with { Blocked = _conversation.IsBlocked });
    }

    [Fact]
    public async Task HandleAsync_Should_UnblockRecordAndPush_When_TheOwnerUnblocksWhatTheyBlocked()
    {
        // Arrange
        _conversation.Block(ConversationParty.Owner, ConversationBuilder.DefaultNow);

        // Act
        var result = await _sut.HandleAsync(OwnerUnblocks(), CancellationToken.None);

        // Assert
        result.Value!.Blocked.Should().BeFalse();
        _conversation.IsBlocked.Should().BeFalse();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.ConversationUnblocked && r.Details!["by"] == "owner"),
            It.IsAny<CancellationToken>()), Times.Once);
        _realtime.Verify(r => r.BlockChangedAsync(
            It.IsAny<IReadOnlyCollection<Guid>>(),
            new ConversationBlockResponse(_conversation.Id.Value, false),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnUnblockForbiddenWithoutSaving_When_TheHostBlocked()
    {
        // Arrange
        _conversation.Block(ConversationParty.Host, ConversationBuilder.DefaultNow);

        // Act
        var result = await _sut.HandleAsync(OwnerUnblocks(), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.UnblockForbidden);
        _conversation.IsBlocked.Should().BeTrue();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnTheConversationWithoutSaving_When_ItIsAlreadyOpen()
    {
        // Act
        var result = await _sut.HandleAsync(OwnerUnblocks(), CancellationToken.None);

        // Assert
        result.Value!.Blocked.Should().BeFalse();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _realtime.Verify(r => r.BlockChangedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<ConversationBlockResponse>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private UnblockConversationCommand OwnerUnblocks() => new(ConversationBuilder.OwnerUserId, Roles.Owner, _conversation.Id.Value);
}
