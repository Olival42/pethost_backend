using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Application.Conversations.LiftModerationBlock;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Conversations.LiftModerationBlock;

public sealed class LiftModerationBlockCommandHandlerTests
{
    private static readonly DateTimeOffset Now = ConversationBuilder.DefaultNow.AddDays(2);
    private static readonly Guid AdminId = TestIds.Of(900);

    private readonly Mock<IConversationRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IConversationsUnitOfWork> _unitOfWork = new(MockBehavior.Strict);
    private readonly Mock<IConversationParties> _parties = new(MockBehavior.Strict);
    private readonly Mock<IConversationQueries> _queries = new(MockBehavior.Strict);
    private readonly Mock<IConversationRealtime> _realtime = new(MockBehavior.Strict);
    private readonly Mock<IAuditTrail> _auditTrail = new(MockBehavior.Strict);
    private readonly Conversation _conversation = new ConversationBuilder().Build();
    private readonly LiftModerationBlockCommandHandler _sut;

    private bool _locked;
    private bool _savedWhileLocked;

    public LiftModerationBlockCommandHandlerTests()
    {
        _sut = new LiftModerationBlockCommandHandler(
            _repository.Object,
            _unitOfWork.Object,
            new ConversationNotifications(_parties.Object, _queries.Object, _realtime.Object),
            _auditTrail.Object,
            new FakeTimeProvider(Now));

        _conversation.BlockByModeration(ConversationBuilder.DefaultNow.AddDays(1));
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
        _parties.Setup(p => p.GetOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Owners());
        _parties.Setup(p => p.GetHostsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Hosts());
        _realtime.Setup(r => r.BlockChangedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<ConversationBlockResponse>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task HandleAsync_Should_LiftInsideTheLockRecordAndPush_When_ModerationBlocked()
    {
        // Act
        var result = await _sut.HandleAsync(new LiftModerationBlockCommand(AdminId, _conversation.Id.Value), CancellationToken.None);

        // Assert
        result.Value.Should().Be(new ConversationBlockResponse(_conversation.Id.Value, false));
        _conversation.IsBlockedByModeration.Should().BeFalse();
        _savedWhileLocked.Should().BeTrue();
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.ConversationUnblocked && r.Details!["by"] == "moderation"),
            It.IsAny<CancellationToken>()), Times.Once);
        _realtime.Verify(r => r.BlockChangedAsync(
            It.IsAny<IReadOnlyCollection<Guid>>(),
            new ConversationBlockResponse(_conversation.Id.Value, false),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_SayItIsStillBlocked_When_ASideAlsoBlocked()
    {
        // Arrange
        _conversation.Block(ConversationParty.Host, Now);

        // Act
        var result = await _sut.HandleAsync(new LiftModerationBlockCommand(AdminId, _conversation.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.Blocked.Should().BeTrue("o bloqueio da anfitriã continua");
    }

    [Fact]
    public async Task HandleAsync_Should_ChangeNothing_When_ModerationDidNotBlock()
    {
        // Arrange
        _conversation.LiftModerationBlock(Now);

        // Act
        var result = await _sut.HandleAsync(new LiftModerationBlockCommand(AdminId, _conversation.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.Blocked.Should().BeFalse();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_TheConversationDoesNotExist()
    {
        // Arrange
        var missing = new ConversationId(TestIds.Of(404));
        _repository.Setup(r => r.GetByIdAsync(missing, It.IsAny<CancellationToken>())).ReturnsAsync((Conversation?)null);

        // Act
        var result = await _sut.HandleAsync(new LiftModerationBlockCommand(AdminId, missing.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.NotFound(missing));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnTheSaveErrorWithoutRecording_When_SavingFails()
    {
        // Arrange
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(ConversationsErrors.Conflict));

        // Act
        var result = await _sut.HandleAsync(new LiftModerationBlockCommand(AdminId, _conversation.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.Conflict);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
