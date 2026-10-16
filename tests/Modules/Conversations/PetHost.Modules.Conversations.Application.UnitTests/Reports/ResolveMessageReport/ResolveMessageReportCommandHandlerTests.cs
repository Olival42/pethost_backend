using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Conversations;
using PetHost.Modules.Conversations.Application.Conversations.Responses;
using PetHost.Modules.Conversations.Application.Reports.ResolveMessageReport;
using PetHost.Modules.Conversations.Application.Reports.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Modules.Conversations.Domain.Reports;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Reports.ResolveMessageReport;

public sealed class ResolveMessageReportCommandHandlerTests
{
    private static readonly DateTimeOffset Now = ReportsTestData.ReportedAt.AddDays(1);
    private static readonly Guid AdminId = TestIds.Of(900);

    private readonly Mock<IMessageReportRepository> _reports = new(MockBehavior.Strict);
    private readonly Mock<IConversationRepository> _conversations = new(MockBehavior.Strict);
    private readonly Mock<IConversationsUnitOfWork> _unitOfWork = new(MockBehavior.Strict);
    private readonly Mock<IConversationQueries> _queries = new(MockBehavior.Strict);
    private readonly Mock<IConversationParties> _parties = new(MockBehavior.Strict);
    private readonly Mock<IConversationRealtime> _realtime = new(MockBehavior.Strict);
    private readonly Mock<IAuditTrail> _auditTrail = new(MockBehavior.Strict);
    private readonly Conversation _conversation = new ConversationBuilder().Build();
    private readonly Message _message;
    private readonly MessageReport _report;
    private readonly ResolveMessageReportCommandHandler _sut;

    private bool _locked;
    private bool _savedWhileLocked;

    public ResolveMessageReportCommandHandlerTests()
    {
        _message = ReportsTestData.HostMessage(_conversation);
        _report = ReportsTestData.Report(_conversation, _message);
        _sut = new ResolveMessageReportCommandHandler(
            _reports.Object,
            _conversations.Object,
            _unitOfWork.Object,
            _queries.Object,
            new MessageReportResponseBuilder(_parties.Object),
            new ConversationNotifications(_parties.Object, _queries.Object, _realtime.Object),
            _auditTrail.Object,
            new FakeTimeProvider(Now));

        _reports.Setup(r => r.GetByIdAsync(_report.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_report);
        _conversations.Setup(c => c.GetByIdAsync(_conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_conversation);
        _conversations.Setup(c => c.ReloadAsync(_conversation, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _unitOfWork
            .Setup(u => u.RunLockedOnConversationAsync(It.IsAny<ConversationId>(), It.IsAny<Func<CancellationToken, Task<Result<Conversation?>>>>(), It.IsAny<CancellationToken>()))
            .Returns<ConversationId, Func<CancellationToken, Task<Result<Conversation?>>>, CancellationToken>(async (_, work, ct) =>
            {
                _locked = true;
                var result = await work(ct);
                _locked = false;
                return result;
            });
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => _savedWhileLocked = _locked).ReturnsAsync(Result.Success());
        _auditTrail.Setup(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _realtime.Setup(r => r.BlockChangedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<ConversationBlockResponse>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _queries.Setup(q => q.GetReportAsync(_report.Id, It.IsAny<CancellationToken>())).ReturnsAsync(() => ReportsTestData.Row(_conversation, _report, _message));
        _parties.Setup(p => p.GetOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Owners());
        _parties.Setup(p => p.GetHostsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Hosts());
    }

    [Fact]
    public async Task HandleAsync_Should_CloseTheReportAndReturnIt_When_TheAdminUpholdsIt()
    {
        // Act
        var result = await _sut.HandleAsync(Command("upheld", "Golpe confirmado."), CancellationToken.None);

        // Assert
        var response = result.Value!;
        response.Status.Should().Be("upheld");
        response.ResolvedAt.Should().Be(Now);
        response.ResolvedBy.Should().Be(AdminId);
        response.ResolutionNote.Should().Be("Golpe confirmado.");
        response.Context.Should().BeNull("o contexto só vem no detalhe");
    }

    [Fact]
    public async Task HandleAsync_Should_BlockTheConversationByModerationInsideTheLock_When_TheAdminUpholdsIt()
    {
        // Act
        await _sut.HandleAsync(Command("upheld", "Golpe confirmado."), CancellationToken.None);

        // Assert
        _conversation.IsBlockedByModeration.Should().BeTrue();
        _conversation.ModerationBlockedAt.Should().Be(Now);
        _conversation.BlockedBy.Should().BeNull("o bloqueio da moderação não é de nenhum dos lados");
        _savedWhileLocked.Should().BeTrue("a análise e o bloqueio gravam juntos, na trava do envio");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _conversations.Verify(c => c.ReloadAsync(_conversation, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_RecordTheModerationBlockAndPushToBothSides_When_TheAdminUpholdsIt()
    {
        // Act
        await _sut.HandleAsync(Command("upheld", "Golpe confirmado."), CancellationToken.None);

        // Assert
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r =>
                r.Action == AuditActions.ConversationBlocked
                && r.TargetType == AuditTargets.Conversation
                && r.TargetId == _conversation.Id.Value
                && r.Details!["by"] == "moderation"),
            It.IsAny<CancellationToken>()), Times.Once);
        _realtime.Verify(r => r.BlockChangedAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2),
            new ConversationBlockResponse(_conversation.Id.Value, true),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_NotRecordOrPushTheBlockAgain_When_TheConversationWasAlreadyBlockedByModeration()
    {
        // Arrange
        _conversation.BlockByModeration(Now.AddDays(-1));

        // Act
        var result = await _sut.HandleAsync(Command("upheld", "Outra denúncia procedente."), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _conversation.ModerationBlockedAt.Should().Be(Now.AddDays(-1));
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.ConversationBlocked), It.IsAny<CancellationToken>()), Times.Never);
        _realtime.Verify(r => r.BlockChangedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<ConversationBlockResponse>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_LeaveTheConversationAlone_When_TheAdminDismissesIt()
    {
        // Act
        var result = await _sut.HandleAsync(Command("dismissed", "Mensagem normal."), CancellationToken.None);

        // Assert
        result.Value!.Status.Should().Be("dismissed");
        _conversation.IsBlocked.Should().BeFalse();
        _conversations.Verify(c => c.GetByIdAsync(It.IsAny<ConversationId>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.RunLockedOnConversationAsync(It.IsAny<ConversationId>(), It.IsAny<Func<CancellationToken, Task<Result<Conversation?>>>>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_RecordTheOutcomeWithTheNoteAsReason_When_Resolved()
    {
        // Act
        await _sut.HandleAsync(Command("dismissed", "Mensagem normal."), CancellationToken.None);

        // Assert
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r =>
                r.Action == AuditActions.ConversationReportResolved
                && r.TargetType == AuditTargets.ConversationReport
                && r.TargetId == _report.Id.Value
                && r.Reason == "Mensagem normal."
                && r.Details!["outcome"] == "dismissed"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_TheReportDoesNotExist()
    {
        // Arrange
        var missing = new MessageReportId(TestIds.Of(404));
        _reports.Setup(r => r.GetByIdAsync(missing, It.IsAny<CancellationToken>())).ReturnsAsync((MessageReport?)null);

        // Act
        var result = await _sut.HandleAsync(new ResolveMessageReportCommand(AdminId, missing.Value, "upheld", "Nota"), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.ReportNotFound(missing));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnAlreadyResolvedWithoutSaving_When_TheReportWasClosedBefore()
    {
        // Arrange
        _report.Resolve(ReportStatus.Dismissed, AdminId, "Já vi.", Now.AddHours(-1));

        // Act
        var result = await _sut.HandleAsync(Command("upheld", "De novo."), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.ReportAlreadyResolved);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _conversation.IsBlocked.Should().BeFalse();
    }

    [Theory]
    [InlineData("upheld")]
    [InlineData("dismissed")]
    public async Task HandleAsync_Should_ReturnConflictWithoutRecordingOrPushing_When_AnotherAdminResolvedAtTheSameTime(string outcome)
    {
        // Arrange
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(ConversationsErrors.Conflict));

        // Act
        var result = await _sut.HandleAsync(Command(outcome, "Nota"), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.Conflict);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        _realtime.Verify(r => r.BlockChangedAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<ConversationBlockResponse>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnConflictWithoutSaving_When_TheConversationIsGone()
    {
        // Arrange
        _conversations.Setup(c => c.GetByIdAsync(_conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Conversation?)null);

        // Act
        var result = await _sut.HandleAsync(Command("upheld", "Nota"), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.Conflict);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnOutcomeInvalidWithoutLoading_When_TheValidatorWasSkipped()
    {
        // Act
        var result = await _sut.HandleAsync(Command("pending", "Nota"), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.OutcomeInvalid);
        _reports.Verify(r => r.GetByIdAsync(It.IsAny<MessageReportId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_TheReportVanishesBeforeTheRead()
    {
        // Arrange
        _queries.Setup(q => q.GetReportAsync(_report.Id, It.IsAny<CancellationToken>())).ReturnsAsync((ReportRow?)null);

        // Act
        var result = await _sut.HandleAsync(Command("dismissed", "Nota"), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.ReportNotFound(_report.Id));
    }

    private ResolveMessageReportCommand Command(string outcome, string note) => new(AdminId, _report.Id.Value, outcome, note);
}
