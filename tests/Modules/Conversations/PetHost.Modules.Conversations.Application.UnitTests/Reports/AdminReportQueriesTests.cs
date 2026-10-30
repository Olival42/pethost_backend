using FluentAssertions;
using Moq;
using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Modules.Conversations.Application.Reports.GetMessageReportById;
using PetHost.Modules.Conversations.Application.Reports.ListMessageReports;
using PetHost.Modules.Conversations.Application.Reports.Responses;
using PetHost.Modules.Conversations.Domain.Conversations;
using PetHost.Modules.Conversations.Domain.Errors;
using PetHost.Modules.Conversations.Domain.Messages;
using PetHost.Modules.Conversations.Domain.Reports;
using PetHost.Shared.Contracts.Audit;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Conversations.Application.UnitTests.Reports;

/// <summary>A fila e o detalhe do admin: filtros, contexto, quem é quem e a trilha.</summary>
public sealed class AdminReportQueriesTests
{
    private readonly Mock<IConversationQueries> _queries = new(MockBehavior.Strict);
    private readonly Mock<IConversationParties> _parties = new(MockBehavior.Strict);
    private readonly Mock<IAuditTrail> _auditTrail = new(MockBehavior.Strict);
    private readonly Conversation _conversation = new ConversationBuilder().Build();
    private readonly Message _message;
    private readonly MessageReport _report;
    private readonly ReportRow _row;

    public AdminReportQueriesTests()
    {
        _message = ReportsTestData.HostMessage(_conversation);
        _report = ReportsTestData.Report(_conversation, _message);
        _row = ReportsTestData.Row(_conversation, _report, _message);

        _parties.Setup(p => p.GetOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Owners());
        _parties.Setup(p => p.GetHostsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(ConversationsTestData.Hosts());
        _auditTrail.Setup(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    private ListMessageReportsQueryHandler List => new(_queries.Object, new MessageReportResponseBuilder(_parties.Object, ConversationsTestData.PhotoLinks()), _auditTrail.Object);

    private GetMessageReportByIdQueryHandler Get => new(_queries.Object, new MessageReportResponseBuilder(_parties.Object, ConversationsTestData.PhotoLinks()), _auditTrail.Object);

    [Fact]
    public async Task List_Should_ReturnThePageWithReporterAndReported_When_FilteredByPending()
    {
        // Arrange
        _queries.Setup(q => q.ListReportsAsync(ReportStatus.Pending, 1, 20, It.IsAny<CancellationToken>())).ReturnsAsync(([_row], 1L));

        // Act
        var result = await List.HandleAsync(new ListMessageReportsQuery("pending"), CancellationToken.None);

        // Assert
        var page = result.Value!;
        page.TotalCount.Should().Be(1);
        var item = page.Items.Single();
        item.Id.Should().Be(_report.Id.Value);
        item.Status.Should().Be("pending");
        item.Reason.Should().Be("scam");
        item.Description.Should().Be("Pediu Pix por fora.");
        item.ReportedBy.Should().Be("owner");
        item.Reporter!.Name.Should().Be("Camila Souza");
        item.Reported!.Id.Should().Be(ConversationBuilder.HostId, "quem enviou a mensagem é quem o admin pode suspender");
        item.Message.Body.Should().Be(_message.Body);
        item.Message.SentBy.Should().Be("host");
        item.Context.Should().BeNull();
    }

    [Fact]
    public async Task List_Should_SwapReporterAndReported_When_TheHostReported()
    {
        // Arrange
        var ownerMessage = ConversationBuilder.Text(_conversation, ConversationParty.Owner);
        var hostReport = MessageReport.Submit(ownerMessage, ConversationParty.Host, ReportReason.Harassment, null, ReportsTestData.ReportedAt).Value!;
        _queries.Setup(q => q.ListReportsAsync(null, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(([ReportsTestData.Row(_conversation, hostReport, ownerMessage)], 1L));

        // Act
        var result = await List.HandleAsync(new ListMessageReportsQuery(null), CancellationToken.None);

        // Assert
        var item = result.Value!.Items.Single();
        item.ReportedBy.Should().Be("host");
        item.Reporter!.Id.Should().Be(ConversationBuilder.HostId);
        item.Reported!.Id.Should().Be(ConversationBuilder.OwnerId);
        item.Description.Should().BeNull();
    }

    [Fact]
    public async Task List_Should_RecordTheListingInTheTrail_When_TheAdminLists()
    {
        // Arrange
        _queries.Setup(q => q.ListReportsAsync(ReportStatus.Pending, 2, 10, It.IsAny<CancellationToken>())).ReturnsAsync(([_row], 11L));

        // Act
        await List.HandleAsync(new ListMessageReportsQuery("pending", 2, 10), CancellationToken.None);

        // Assert
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r =>
                r.Action == AuditActions.ConversationReportsListed
                && r.TargetType == AuditTargets.ConversationReport
                && r.TargetId == null
                && r.Details!["status"] == "pending"
                && r.Details["page"] == "2"
                && r.Details["count"] == "1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_Should_ReturnAllFieldErrorsWithoutReading_When_TheFiltersAreInvalid()
    {
        // Act
        var result = await List.HandleAsync(new ListMessageReportsQuery("closed", 0, 101), CancellationToken.None);

        // Assert
        result.Errors.Should().BeEquivalentTo([ConversationsErrors.StatusInvalid, ConversationsErrors.PageInvalid, ConversationsErrors.PageSizeInvalid]);
        _queries.Verify(q => q.ListReportsAsync(It.IsAny<ReportStatus?>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_Should_ReturnAnEmptyPageWithoutLookingUpPeople_When_ThereAreNoReports()
    {
        // Arrange
        _queries.Setup(q => q.ListReportsAsync(ReportStatus.Upheld, 1, 20, It.IsAny<CancellationToken>())).ReturnsAsync(([], 0L));

        // Act
        var result = await List.HandleAsync(new ListMessageReportsQuery("upheld"), CancellationToken.None);

        // Assert
        result.Value!.Items.Should().BeEmpty();
        _parties.Verify(p => p.GetOwnersAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Get_Should_ReturnTheReportWithTheEarlierMessagesAsContext_When_ItExists()
    {
        // Arrange
        var earlier = ConversationsTestData.MessageRow(_conversation, ConversationParty.Owner, "Quanto fica a diária?", ConversationBuilder.DefaultNow.AddMinutes(-5));
        _queries.Setup(q => q.GetReportAsync(_report.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_row);
        _queries
            .Setup(q => q.ListMessagesAsync(_conversation.Id, _message.Id, GetMessageReportByIdQueryHandler.ContextSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync([earlier]);

        // Act
        var result = await Get.HandleAsync(new GetMessageReportByIdQuery(_report.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.Context.Should().ContainSingle().Which.Body.Should().Be("Quanto fica a diária?");
        result.Value.Message.Id.Should().Be(_message.Id.Value);
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.ConversationReportViewed && r.TargetId == _report.Id.Value),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Get_Should_ReturnAnEmptyContext_When_TheReportedMessageIsTheFirst()
    {
        // Arrange
        _queries.Setup(q => q.GetReportAsync(_report.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_row);
        _queries
            .Setup(q => q.ListMessagesAsync(_conversation.Id, _message.Id, GetMessageReportByIdQueryHandler.ContextSize, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Act
        var result = await Get.HandleAsync(new GetMessageReportByIdQuery(_report.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.Context.Should().BeEmpty();
    }

    [Fact]
    public async Task Get_Should_ReturnNotFoundWithoutRecording_When_TheReportDoesNotExist()
    {
        // Arrange
        var missing = new MessageReportId(TestIds.Of(404));
        _queries.Setup(q => q.GetReportAsync(missing, It.IsAny<CancellationToken>())).ReturnsAsync((ReportRow?)null);

        // Act
        var result = await Get.HandleAsync(new GetMessageReportByIdQuery(missing.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ConversationsErrors.ReportNotFound(missing));
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
