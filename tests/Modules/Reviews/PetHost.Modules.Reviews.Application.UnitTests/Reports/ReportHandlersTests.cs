using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Application.Reports.GetReviewReportById;
using PetHost.Modules.Reviews.Application.Reports.ListReviewReports;
using PetHost.Modules.Reviews.Application.Reports.ReportReview;
using PetHost.Modules.Reviews.Application.Reports.ResolveReviewReport;
using PetHost.Modules.Reviews.Application.Reports.Responses;
using PetHost.Modules.Reviews.Application.Reviews.Responses;
using PetHost.Modules.Reviews.Application.Reviews.UnhideReview;
using PetHost.Modules.Reviews.Domain.Errors;
using PetHost.Modules.Reviews.Domain.Reports;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Reviews.Application.UnitTests.Reports;

/// <summary>Denúncia (por quem vê) e moderação do admin (fila, análise, voltar a mostrar).</summary>
public sealed class ReportHandlersTests
{
    private static readonly DateTimeOffset Now = ReviewBuilder.DefaultNow;
    private static readonly Guid AdminUserId = Guid.Parse("0199d000-0000-7000-8000-0000000000aa");
    private static readonly Guid StrangerUserId = Guid.Parse("0199d000-0000-7000-8000-0000000000bb");

    private readonly Mock<IReviewRepository> _reviews = new(MockBehavior.Strict);
    private readonly Mock<IReviewReportRepository> _reports = new(MockBehavior.Strict);
    private readonly Mock<IReviewsUnitOfWork> _unitOfWork = new(MockBehavior.Strict);
    private readonly Mock<IReviewParties> _parties = ReviewsTestData.Parties();
    private readonly Mock<IReviewNotifier> _notifier = new(MockBehavior.Strict);
    private readonly Mock<IReviewQueries> _queries = new(MockBehavior.Strict);
    private readonly Mock<IAuditTrail> _auditTrail = new(MockBehavior.Strict);
    private readonly Review _ownerReview = new ReviewBuilder().Build();
    private readonly Review _hostReview = new ReviewBuilder().ByHost().Build();
    private readonly ReviewReportResponseBuilder _reportBuilder;

    private ReviewReport? _added;

    public ReportHandlersTests()
    {
        _reportBuilder = new ReviewReportResponseBuilder(_queries.Object, new ReviewResponseBuilder(_parties.Object));

        _ownerReview.Publish(Now);
        _hostReview.Publish(Now);
        _reviews.Setup(r => r.GetByIdAsync(_ownerReview.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_ownerReview);
        _reviews.Setup(r => r.GetByIdAsync(_hostReview.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_hostReview);
        _reports.Setup(r => r.Add(It.IsAny<ReviewReport>())).Callback<ReviewReport>(r => _added = r);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        _auditTrail.Setup(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _notifier.Setup(n => n.AlertModeratorsAsync(It.IsAny<ReviewReportId>(), It.IsAny<ReviewReportReason>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _queries
            .Setup(q => q.GetManyAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, ReviewRow> { [_ownerReview.Id.Value] = ReviewsTestData.Row(_ownerReview) });
        _parties.Setup(p => p.FindOwnerByAccountAsync(StrangerUserId, It.IsAny<CancellationToken>())).ReturnsAsync(new ReviewerProfile(Guid.CreateVersion7(), true));
    }

    private ReportReviewCommandHandler ReportSut() =>
        new(_reviews.Object, _reports.Object, _unitOfWork.Object, _parties.Object, _notifier.Object, _auditTrail.Object, new FakeTimeProvider(Now));

    private ResolveReviewReportCommandHandler ResolveSut() =>
        new(_reports.Object, _reviews.Object, _unitOfWork.Object, _reportBuilder, _auditTrail.Object, new FakeTimeProvider(Now));

    private ReviewReport PendingReport() =>
        ReviewReport.Submit(_ownerReview, ReviewParty.Host, ReviewBuilder.HostId, ReviewBuilder.HostUserId, ReviewReportReason.Offensive, null, Now).Value!;

    // ----- ReportReview -----

    [Fact]
    public async Task Report_Should_SaveRecordAndAlertModeration_When_TheHostReportsTheOwnersReview()
    {
        // Act
        var result = await ReportSut().HandleAsync(
            new ReportReviewCommand(ReviewBuilder.HostUserId, Roles.Host, _ownerReview.Id.Value, "offensive", null),
            CancellationToken.None);

        // Assert
        result.Value.Should().Be(new SubmittedReviewReportResponse(_added!.Id.Value, "pending"));
        _added.ReportedBy.Should().Be(ReviewBuilder.HostUserId);
        _auditTrail.Verify(
            a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.ReviewReported && r.Details!["reason"] == "offensive"), It.IsAny<CancellationToken>()),
            Times.Once);
        _notifier.Verify(n => n.AlertModeratorsAsync(_added.Id, ReviewReportReason.Offensive, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Report_Should_LetAnyOwnerReport_When_TheReviewIsAboutAHost()
    {
        // Act
        var result = await ReportSut().HandleAsync(
            new ReportReviewCommand(StrangerUserId, Roles.Owner, _ownerReview.Id.Value, "spam", null),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Report_Should_ReturnNotFound_When_AnotherOwnerReportsTheHostsReview()
    {
        // Act
        var result = await ReportSut().HandleAsync(
            new ReportReviewCommand(StrangerUserId, Roles.Owner, _hostReview.Id.Value, "spam", null),
            CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NotFound(_hostReview.Id));
        _reports.Verify(r => r.Add(It.IsAny<ReviewReport>()), Times.Never);
    }

    [Fact]
    public async Task Report_Should_LetTheReviewedOwnerReport_When_TheHostWroteAboutHer()
    {
        // Act
        var result = await ReportSut().HandleAsync(
            new ReportReviewCommand(ReviewBuilder.OwnerUserId, Roles.Owner, _hostReview.Id.Value, "false_information", null),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Report_Should_ReturnNotFound_When_TheReviewIsNotPublishedYet()
    {
        // Arrange
        var held = new ReviewBuilder().Build();
        _reviews.Setup(r => r.GetByIdAsync(held.Id, It.IsAny<CancellationToken>())).ReturnsAsync(held);

        // Act
        var result = await ReportSut().HandleAsync(
            new ReportReviewCommand(ReviewBuilder.HostUserId, Roles.Host, held.Id.Value, "spam", null),
            CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NotFound(held.Id));
    }

    [Fact]
    public async Task Report_Should_ReturnNotFound_When_TheReviewIsHidden()
    {
        // Arrange
        _ownerReview.Hide(AdminUserId, "Já oculta", Now);

        // Act
        var result = await ReportSut().HandleAsync(
            new ReportReviewCommand(ReviewBuilder.HostUserId, Roles.Host, _ownerReview.Id.Value, "spam", null),
            CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NotFound(_ownerReview.Id));
    }

    [Fact]
    public async Task Report_Should_ReturnReportOwnReview_When_TheAuthorReports()
    {
        // Act
        var result = await ReportSut().HandleAsync(
            new ReportReviewCommand(ReviewBuilder.OwnerUserId, Roles.Owner, _ownerReview.Id.Value, "spam", null),
            CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.ReportOwnReview);
    }

    [Fact]
    public async Task Report_Should_ReturnReportAlreadyExists_When_TheDatabaseFindsADuplicate()
    {
        // Arrange
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(ReviewsErrors.ReportAlreadyExists));

        // Act
        var result = await ReportSut().HandleAsync(
            new ReportReviewCommand(ReviewBuilder.HostUserId, Roles.Host, _ownerReview.Id.Value, "spam", null),
            CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.ReportAlreadyExists);
        _notifier.Verify(n => n.AlertModeratorsAsync(It.IsAny<ReviewReportId>(), It.IsAny<ReviewReportReason>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Report_Should_ReturnHostDeactivated_When_TheHostIsSuspended()
    {
        // Arrange
        _parties.Setup(p => p.FindHostByAccountAsync(ReviewBuilder.HostUserId, It.IsAny<CancellationToken>())).ReturnsAsync(ReviewsTestData.UnavailableHost);

        // Act
        var result = await ReportSut().HandleAsync(
            new ReportReviewCommand(ReviewBuilder.HostUserId, Roles.Host, _ownerReview.Id.Value, "spam", null),
            CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.HostDeactivated);
    }

    [Fact]
    public async Task Report_Should_ReturnNotFound_When_TheReviewDoesNotExist()
    {
        // Arrange
        var id = new ReviewId(Guid.CreateVersion7());
        _reviews.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((Review?)null);

        // Act
        var result = await ReportSut().HandleAsync(new ReportReviewCommand(ReviewBuilder.HostUserId, Roles.Host, id.Value, "spam", null), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NotFound(id));
    }

    // ----- ResolveReviewReport -----

    [Fact]
    public async Task Resolve_Should_HideTheReviewWithTheNoteAndRecordBoth_When_Upheld()
    {
        // Arrange
        var report = PendingReport();
        _reports.Setup(r => r.GetByIdAsync(report.Id, It.IsAny<CancellationToken>())).ReturnsAsync(report);

        // Act
        var result = await ResolveSut().HandleAsync(new ResolveReviewReportCommand(AdminUserId, report.Id.Value, "upheld", "Linguagem ofensiva."), CancellationToken.None);

        // Assert
        result.Value!.Status.Should().Be("upheld");
        result.Value.Review!.Id.Should().Be(_ownerReview.Id.Value);
        _ownerReview.HiddenReason.Should().Be("Linguagem ofensiva.");
        _ownerReview.HiddenBy.Should().Be(AdminUserId);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.ReviewReportResolved && r.Reason == "Linguagem ofensiva."), It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.ReviewHidden && r.TargetId == _ownerReview.Id.Value), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Resolve_Should_KeepTheReviewVisible_When_Dismissed()
    {
        // Arrange
        var report = PendingReport();
        _reports.Setup(r => r.GetByIdAsync(report.Id, It.IsAny<CancellationToken>())).ReturnsAsync(report);

        // Act
        var result = await ResolveSut().HandleAsync(new ResolveReviewReportCommand(AdminUserId, report.Id.Value, "dismissed", "Opinião legítima."), CancellationToken.None);

        // Assert
        result.Value!.Status.Should().Be("dismissed");
        _ownerReview.IsHidden.Should().BeFalse();
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.ReviewHidden), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_Should_NotRecordHiding_When_TheReviewWasDeleted()
    {
        // Arrange
        var report = PendingReport();
        _reports.Setup(r => r.GetByIdAsync(report.Id, It.IsAny<CancellationToken>())).ReturnsAsync(report);
        _reviews.Setup(r => r.GetByIdAsync(_ownerReview.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Review?)null);

        // Act
        var result = await ResolveSut().HandleAsync(new ResolveReviewReportCommand(AdminUserId, report.Id.Value, "upheld", "Ok."), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.ReviewHidden), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_Should_ReturnReportNotFound_When_TheReportDoesNotExist()
    {
        // Arrange
        var id = new ReviewReportId(Guid.CreateVersion7());
        _reports.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((ReviewReport?)null);

        // Act
        var result = await ResolveSut().HandleAsync(new ResolveReviewReportCommand(AdminUserId, id.Value, "upheld", "Ok."), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.ReportNotFound(id));
    }

    [Fact]
    public async Task Resolve_Should_ReturnAlreadyResolved_When_TheReportWasResolved()
    {
        // Arrange
        var report = PendingReport();
        report.Resolve(ReviewReportStatus.Dismissed, AdminUserId, "Ok.", Now);
        _reports.Setup(r => r.GetByIdAsync(report.Id, It.IsAny<CancellationToken>())).ReturnsAsync(report);

        // Act
        var result = await ResolveSut().HandleAsync(new ResolveReviewReportCommand(AdminUserId, report.Id.Value, "upheld", "Ok."), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.ReportAlreadyResolved);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resolve_Should_PropagateTheSaveError_When_AnotherAdminResolvedAtTheSameTime()
    {
        // Arrange
        var report = PendingReport();
        _reports.Setup(r => r.GetByIdAsync(report.Id, It.IsAny<CancellationToken>())).ReturnsAsync(report);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(ReviewsErrors.Conflict));

        // Act
        var result = await ResolveSut().HandleAsync(new ResolveReviewReportCommand(AdminUserId, report.Id.Value, "dismissed", "Ok."), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.Conflict);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ----- Fila do admin -----

    [Fact]
    public async Task ListReports_Should_ReturnThePageWithTheReviewsAndRecordIt_When_FilteredByStatus()
    {
        // Arrange
        var row = ReviewReportResponseBuilder.ToRow(PendingReport());
        _queries.Setup(q => q.ListReportsAsync(ReviewReportStatus.Pending, 1, 20, It.IsAny<CancellationToken>())).ReturnsAsync(([row], 1L));
        var sut = new ListReviewReportsQueryHandler(_queries.Object, _reportBuilder, _auditTrail.Object);

        // Act
        var result = await sut.HandleAsync(new ListReviewReportsQuery("pending"), CancellationToken.None);

        // Assert
        var item = result.Value!.Items.Should().ContainSingle().Subject;
        item.Reason.Should().Be("offensive");
        item.ReportedBy.Should().Be("host");
        item.Review!.BookingId.Should().Be(ReviewBuilder.BookingId);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.ReviewReportsListed && r.Details!["count"] == "1"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListReports_Should_ListEverything_When_ThereIsNoStatus()
    {
        // Arrange
        _queries.Setup(q => q.ListReportsAsync(null, 1, 20, It.IsAny<CancellationToken>())).ReturnsAsync(([], 0L));
        var sut = new ListReviewReportsQueryHandler(_queries.Object, _reportBuilder, _auditTrail.Object);

        // Act
        var result = await sut.HandleAsync(new ListReviewReportsQuery(null), CancellationToken.None);

        // Assert
        result.Value!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ListReports_Should_ReturnValidationErrors_When_StatusAndPagingAreInvalid()
    {
        // Arrange
        var sut = new ListReviewReportsQueryHandler(_queries.Object, _reportBuilder, _auditTrail.Object);

        // Act
        var result = await sut.HandleAsync(new ListReviewReportsQuery("open", 0), CancellationToken.None);

        // Assert
        result.Errors.Should().BeEquivalentTo([ReviewsErrors.PageInvalid, ReviewsErrors.StatusInvalid]);
    }

    [Fact]
    public async Task GetReport_Should_ReturnTheReportAndRecordTheView_When_ItExists()
    {
        // Arrange
        var row = ReviewReportResponseBuilder.ToRow(PendingReport());
        _queries.Setup(q => q.GetReportAsync(row.Id.Value, It.IsAny<CancellationToken>())).ReturnsAsync(row);
        var sut = new GetReviewReportByIdQueryHandler(_queries.Object, _reportBuilder, _auditTrail.Object);

        // Act
        var result = await sut.HandleAsync(new GetReviewReportByIdQuery(row.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.Id.Should().Be(row.Id.Value);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.ReviewReportViewed && r.TargetId == row.Id.Value), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetReport_Should_ReturnReportNotFound_When_ItDoesNotExist()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        _queries.Setup(q => q.GetReportAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((ReviewReportRow?)null);
        var sut = new GetReviewReportByIdQueryHandler(_queries.Object, _reportBuilder, _auditTrail.Object);

        // Act
        var result = await sut.HandleAsync(new GetReviewReportByIdQuery(id), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.ReportNotFound(new ReviewReportId(id)));
    }

    [Fact]
    public async Task ReportBuilder_Should_LeaveTheReviewEmpty_When_TheAuthorDeletedIt()
    {
        // Arrange
        _queries.Setup(q => q.GetManyAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new Dictionary<Guid, ReviewRow>());

        // Act
        var response = await _reportBuilder.BuildAsync(ReviewReportResponseBuilder.ToRow(PendingReport()), CancellationToken.None);

        // Assert
        response.Review.Should().BeNull();
    }

    // ----- UnhideReview -----

    [Fact]
    public async Task Unhide_Should_ShowTheReviewAgainAndRecord_When_ItIsHidden()
    {
        // Arrange
        _ownerReview.Hide(AdminUserId, "Engano", Now);
        var sut = new UnhideReviewCommandHandler(_reviews.Object, _unitOfWork.Object, new ReviewResponseBuilder(_parties.Object), _auditTrail.Object, new FakeTimeProvider(Now));

        // Act
        var result = await sut.HandleAsync(new UnhideReviewCommand(AdminUserId, _ownerReview.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.Moderation.Should().BeNull();
        _ownerReview.IsHidden.Should().BeFalse();
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.ReviewUnhidden), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Unhide_Should_NotSave_When_TheReviewIsVisible()
    {
        // Arrange
        var sut = new UnhideReviewCommandHandler(_reviews.Object, _unitOfWork.Object, new ReviewResponseBuilder(_parties.Object), _auditTrail.Object, new FakeTimeProvider(Now));

        // Act
        var result = await sut.HandleAsync(new UnhideReviewCommand(AdminUserId, _ownerReview.Id.Value), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unhide_Should_ReturnNotFound_When_TheReviewDoesNotExist()
    {
        // Arrange
        var id = new ReviewId(Guid.CreateVersion7());
        _reviews.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync((Review?)null);
        var sut = new UnhideReviewCommandHandler(_reviews.Object, _unitOfWork.Object, new ReviewResponseBuilder(_parties.Object), _auditTrail.Object, new FakeTimeProvider(Now));

        // Act
        var result = await sut.HandleAsync(new UnhideReviewCommand(AdminUserId, id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.NotFound(id));
    }

    [Fact]
    public async Task Unhide_Should_PropagateTheSaveError_When_SavingFails()
    {
        // Arrange
        _ownerReview.Hide(AdminUserId, "Engano", Now);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result.Failure(ReviewsErrors.Conflict));
        var sut = new UnhideReviewCommandHandler(_reviews.Object, _unitOfWork.Object, new ReviewResponseBuilder(_parties.Object), _auditTrail.Object, new FakeTimeProvider(Now));

        // Act
        var result = await sut.HandleAsync(new UnhideReviewCommand(AdminUserId, _ownerReview.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ReviewsErrors.Conflict);
    }

    [Fact]
    public async Task Handlers_Should_Throw_When_TheInputIsNull()
    {
        // Arrange
        var unhide = new UnhideReviewCommandHandler(_reviews.Object, _unitOfWork.Object, new ReviewResponseBuilder(_parties.Object), _auditTrail.Object, new FakeTimeProvider(Now));

        // Act & Assert
        await ((Func<Task>)(() => ReportSut().HandleAsync(null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => ResolveSut().HandleAsync(null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => unhide.HandleAsync(null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => new ListReviewReportsQueryHandler(_queries.Object, _reportBuilder, _auditTrail.Object).HandleAsync(null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => new GetReviewReportByIdQueryHandler(_queries.Object, _reportBuilder, _auditTrail.Object).HandleAsync(null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => _reportBuilder.BuildAsync((IReadOnlyList<ReviewReportRow>)null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
        ((Action)(() => ReviewReportResponseBuilder.ToRow(null!))).Should().Throw<ArgumentNullException>();
    }
}
