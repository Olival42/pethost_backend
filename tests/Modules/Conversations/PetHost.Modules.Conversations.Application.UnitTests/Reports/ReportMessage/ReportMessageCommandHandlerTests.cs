using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Reports.ReportMessage;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Modules.Conversations.Domain.Reports;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Reports.ReportMessage;

public sealed class ReportMessageCommandHandlerTests
{
    private static readonly DateTimeOffset Now = ReportsTestData.ReportedAt;

    private readonly Mock<IConversationRepository> _repository = new(MockBehavior.Strict);
    private readonly Mock<IMessageRepository> _messages = new(MockBehavior.Strict);
    private readonly Mock<IMessageReportRepository> _reports = new(MockBehavior.Strict);
    private readonly Mock<IConversationsUnitOfWork> _unitOfWork = new(MockBehavior.Strict);
    private readonly Mock<IConversationParties> _parties = new(MockBehavior.Strict);
    private readonly Mock<IConversationQueries> _queries = new(MockBehavior.Strict);
    private readonly Mock<IModerationAlerts> _alerts = new(MockBehavior.Strict);
    private readonly Mock<IAuditTrail> _auditTrail = new(MockBehavior.Strict);
    private readonly Conversation _conversation = new ConversationBuilder().Build();
    private readonly Message _hostMessage;
    private readonly ReportMessageCommandHandler _sut;

    private MessageReport? _added;

    public ReportMessageCommandHandlerTests()
    {
        _hostMessage = ReportsTestData.HostMessage(_conversation);
        _sut = new ReportMessageCommandHandler(
            _repository.Object,
            _messages.Object,
            _reports.Object,
            _unitOfWork.Object,
            _parties.Object,
            _queries.Object,
            _alerts.Object,
            _auditTrail.Object,
            new FakeTimeProvider(Now));

        _parties.Setup(p => p.FindOwnerByAccountAsync(ConversationBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.AvailableOwner);
        _parties.Setup(p => p.FindHostByAccountAsync(ConversationBuilder.HostUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.AvailableHost);
        _repository.Setup(r => r.GetByIdAsync(_conversation.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_conversation);
        _messages.Setup(m => m.FindInConversationAsync(_conversation.Id, _hostMessage.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_hostMessage);
        _reports.Setup(r => r.Add(It.IsAny<MessageReport>())).Callback<MessageReport>(report => _added = report);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        _auditTrail.Setup(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _queries.Setup(q => q.CountPendingReportsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(3);
        _alerts.Setup(a => a.ReportSubmittedAsync(It.IsAny<ReportAlert>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    [Fact]
    public async Task HandleAsync_Should_AlertTheAdminWithTheReasonAndThePendingCount_When_Saved()
    {
        // Act
        await _sut.HandleAsync(OwnerReports("scam", "Pediu Pix por fora."), CancellationToken.None);

        // Assert
        _alerts.Verify(a => a.ReportSubmittedAsync(
            new ReportAlert(_added!.Id.Value, _conversation.Id.Value, "scam", "owner", 3),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_NotAlertTheAdmin_When_SavingFails()
    {
        // Arrange
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(ConversationsErrors.ReportAlreadyExists));

        // Act
        await _sut.HandleAsync(OwnerReports("spam", null), CancellationToken.None);

        // Assert
        _alerts.Verify(a => a.ReportSubmittedAsync(It.IsAny<ReportAlert>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_SaveAPendingReport_When_TheOwnerReportsAMessageFromTheHost()
    {
        // Act
        var result = await _sut.HandleAsync(OwnerReports("scam", "Pediu Pix por fora."), CancellationToken.None);

        // Assert
        var response = result.Value!;
        response.Id.Should().Be(_added!.Id.Value);
        response.ConversationId.Should().Be(_conversation.Id.Value);
        response.MessageId.Should().Be(_hostMessage.Id.Value);
        response.Reason.Should().Be("scam");
        response.Status.Should().Be("pending");
        response.CreatedAt.Should().Be(Now);
        _added.ReportedBy.Should().Be(ConversationParty.Owner);
        _added.Description.Should().Be("Pediu Pix por fora.");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_RecordTheReasonButNotTheDescription_When_Saved()
    {
        // Act
        await _sut.HandleAsync(OwnerReports("scam", "Pediu Pix por fora."), CancellationToken.None);

        // Assert
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r =>
                r.Action == AuditActions.ConversationMessageReported
                && r.TargetType == AuditTargets.ConversationReport
                && r.TargetId == _added!.Id.Value
                && r.Details!["reason"] == "scam"
                && r.Details["reportedBy"] == "owner"
                && r.Details["messageId"] == _hostMessage.Id.Value.ToString()
                && !r.Details.Values.Any(v => v != null && v.Contains("Pix", StringComparison.Ordinal))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnReportOwnMessageWithoutSaving_When_TheHostReportsTheirOwnMessage()
    {
        // Act
        var result = await _sut.HandleAsync(
            new ReportMessageCommand(ConversationBuilder.HostUserId, Roles.Host, _conversation.Id.Value, _hostMessage.Id.Value, "spam", null),
            CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.ReportOwnMessage);
        _reports.Verify(r => r.Add(It.IsAny<MessageReport>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnMessageNotFound_When_TheMessageIsNotInThisConversation()
    {
        // Arrange
        var otherId = new MessageId(TestIds.Of(321));
        _messages.Setup(m => m.FindInConversationAsync(_conversation.Id, otherId, It.IsAny<CancellationToken>())).ReturnsAsync((Message?)null);

        // Act
        var result = await _sut.HandleAsync(
            new ReportMessageCommand(ConversationBuilder.OwnerUserId, Roles.Owner, _conversation.Id.Value, otherId.Value, "spam", null),
            CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.MessageNotFound(otherId));
        _reports.Verify(r => r.Add(It.IsAny<MessageReport>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFoundWithoutLookingForTheMessage_When_TheConversationIsFromAnotherOwner()
    {
        // Arrange
        var someoneElses = new ConversationBuilder().WithOwner(TestIds.Of(77)).Build();
        _repository.Setup(r => r.GetByIdAsync(someoneElses.Id, It.IsAny<CancellationToken>())).ReturnsAsync(someoneElses);

        // Act
        var result = await _sut.HandleAsync(
            new ReportMessageCommand(ConversationBuilder.OwnerUserId, Roles.Owner, someoneElses.Id.Value, _hostMessage.Id.Value, "spam", null),
            CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.NotFound(someoneElses.Id));
        _messages.Verify(m => m.FindInConversationAsync(It.IsAny<ConversationId>(), It.IsAny<MessageId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnAlreadyExistsWithoutRecording_When_TheMessageWasReportedBefore()
    {
        // Arrange
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(ConversationsErrors.ReportAlreadyExists));

        // Act
        var result = await _sut.HandleAsync(OwnerReports("spam", null), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.ReportAlreadyExists);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_StillReport_When_TheOwnerIsSuspended()
    {
        // Arrange
        _parties.Setup(p => p.FindOwnerByAccountAsync(ConversationBuilder.OwnerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.SuspendedOwner);

        // Act
        var result = await _sut.HandleAsync(OwnerReports("harassment", null), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnReasonInvalid_When_TheValidatorWasSkipped()
    {
        // Act
        var result = await _sut.HandleAsync(OwnerReports("fraude", null), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.ReasonInvalid);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnDescriptionRequired_When_TheReasonIsOtherWithoutDescription()
    {
        // Act
        var result = await _sut.HandleAsync(OwnerReports("other", null), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.DescriptionRequired);
        _reports.Verify(r => r.Add(It.IsAny<MessageReport>()), Times.Never);
    }

    private ReportMessageCommand OwnerReports(string reason, string? description) =>
        new(ConversationBuilder.OwnerUserId, Roles.Owner, _conversation.Id.Value, _hostMessage.Id.Value, reason, description);
}
