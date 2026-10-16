using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Application.Conversations.BlockConversation;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Conversations.BlockConversation;

/// <summary>Bloquear (e, por <c>ConversationBlocking</c>, o caminho comum com o desbloqueio).</summary>
public sealed class BlockConversationCommandHandlerTests
{
    private static readonly DateTimeOffset Now = ConversationBuilder.DefaultNow.AddHours(3);

    private readonly Mock<IConversationRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IConversationsUnitOfWork> _unitOfWork = new(MockBehavior.Strict);
    private readonly Mock<IConversationParties> _parties = new(MockBehavior.Strict);
    private readonly Mock<IConversationQueries> _queries = new(MockBehavior.Strict);
    private readonly Mock<IConversationRealtime> _realtime = new(MockBehavior.Strict);
    private readonly Mock<IAuditTrail> _auditTrail = new(MockBehavior.Strict);
    private readonly Conversation _conversation = new ConversationBuilder().Build();
    private readonly BlockConversationCommandHandler _sut;

    private bool _locked;
    private bool _savedWhileLocked;

    public BlockConversationCommandHandlerTests()
    {
        _sut = new BlockConversationCommandHandler(
            _repository.Object,
            _unitOfWork.Object,
            _parties.Object,
            _queries.Object,
            new ConversationResponseBuilder(_parties.Object),
            new ConversationNotifications(_parties.Object, _queries.Object, _realtime.Object),
            _auditTrail.Object,
            new FakeTimeProvider(Now));

        _parties.Setup(p => p.FindOwnerByAccountAsync(ConversationBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.AvailableOwner);
        _parties.Setup(p => p.FindHostByAccountAsync(ConversationBuilder.HostUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.AvailableHost);
        _parties.Setup(p => p.GetOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Owners());
        _parties.Setup(p => p.GetHostsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Hosts());
        _repository.Setup(r => r.GetByIdAsync(_conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_conversation);
        _repository.Setup(r => r.ReloadAsync(_conversation, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _unitOfWork
            .Setup(u => u.RunLockedOnConversationAsync(It.IsAny<ConversationId>(), It.IsAny<Func<CancellationToken, Task<Result<bool>>>>(), It.IsAny<CancellationToken>()))
            .Returns<ConversationId, Func<CancellationToken, Task<Result<bool>>>, CancellationToken>(async (_, work, ct) =>
            {
                _locked = true;
                var result = await work(ct);
                _locked = false;
                return result;
            });
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => _savedWhileLocked = _locked).ReturnsAsync(Result.Success());
        _auditTrail.Setup(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _realtime.Setup(r => r.BlockChangedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<ConversationBlockResponse>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _queries
            .Setup(q => q.GetAsync(_conversation.Id, It.IsAny<ConversationParty>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConversationId _, ConversationParty viewer, CancellationToken _) =>
                ConversationsTestData.Row(_conversation) with { Blocked = _conversation.IsBlocked, BlockedByViewer = _conversation.BlockedBy == viewer });
    }

    [Fact]
    public async Task HandleAsync_Should_BlockInsideTheLockAndReturnTheConversation_When_TheHostBlocks()
    {
        // Act
        var result = await _sut.HandleAsync(HostBlocks(), CancellationToken.None);

        // Assert
        result.Value!.Blocked.Should().BeTrue();
        result.Value.BlockedByMe.Should().BeTrue();
        _conversation.BlockedBy.Should().Be(ConversationParty.Host);
        _conversation.BlockedAt.Should().Be(Now);
        _savedWhileLocked.Should().BeTrue("o bloqueio grava na mesma trava do envio");
        _repository.Verify(r => r.ReloadAsync(_conversation, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_RecordWhoBlockedAndPushToBothAccounts_When_TheHostBlocks()
    {
        // Act
        await _sut.HandleAsync(HostBlocks(), CancellationToken.None);

        // Assert
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r =>
                r.Action == AuditActions.ConversationBlocked
                && r.TargetType == AuditTargets.Conversation
                && r.TargetId == _conversation.Id.Value
                && r.Details!["by"] == "host"),
            It.IsAny<CancellationToken>()), Times.Once);
        _realtime.Verify(r => r.BlockChangedAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(ConversationBuilder.OwnerUserId) && ids.Contains(ConversationBuilder.HostUserId)),
            new ConversationBlockResponse(_conversation.Id.Value, true),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_NotSaveRecordOrPush_When_TheSameSideBlocksAgain()
    {
        // Arrange
        _conversation.Block(ConversationParty.Host, ConversationBuilder.DefaultNow);

        // Act
        var result = await _sut.HandleAsync(HostBlocks(), CancellationToken.None);

        // Assert
        result.Value!.Blocked.Should().BeTrue();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        _realtime.Verify(r => r.BlockChangedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<ConversationBlockResponse>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnAlreadyBlockedWithoutSaving_When_TheOtherSideBlockedFirst()
    {
        // Arrange
        _conversation.Block(ConversationParty.Owner, ConversationBuilder.DefaultNow);

        // Act
        var result = await _sut.HandleAsync(HostBlocks(), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.AlreadyBlocked);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_StillBlock_When_TheHostIsSuspended()
    {
        // Arrange
        _parties.Setup(p => p.FindHostByAccountAsync(ConversationBuilder.HostUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.SuspendedHost);

        // Act
        var result = await _sut.HandleAsync(HostBlocks(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue("bloquear é proteção, não exige perfil ativo");
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_TheConversationIsFromAnotherHost()
    {
        // Arrange
        var someoneElses = new ConversationBuilder().WithHost(TestIds.Of(55)).Build();
        _repository.Setup(r => r.GetByIdAsync(someoneElses.Id, It.IsAny<CancellationToken>())).ReturnsAsync(someoneElses);

        // Act
        var result = await _sut.HandleAsync(new BlockConversationCommand(ConversationBuilder.HostUserId, Roles.Host, someoneElses.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.NotFound(someoneElses.Id));
        someoneElses.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnTheSaveErrorWithoutRecording_When_SavingFails()
    {
        // Arrange
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(ConversationsErrors.Conflict));

        // Act
        var result = await _sut.HandleAsync(HostBlocks(), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.Conflict);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        _realtime.Verify(r => r.BlockChangedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<ConversationBlockResponse>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_TheConversationVanishesBeforeTheRead()
    {
        // Arrange
        _queries.Setup(q => q.GetAsync(_conversation.Id, ConversationParty.Host, It.IsAny<CancellationToken>())).ReturnsAsync((ConversationRow?)null);

        // Act
        var result = await _sut.HandleAsync(HostBlocks(), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.NotFound(_conversation.Id));
    }

    private BlockConversationCommand HostBlocks() => new(ConversationBuilder.HostUserId, Roles.Host, _conversation.Id.Value);
}
