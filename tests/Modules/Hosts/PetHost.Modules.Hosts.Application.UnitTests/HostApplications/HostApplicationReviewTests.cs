using PetHost.Modules.Hosts.Domain.IdentityBlocks;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.HostApplications.ApproveHostApplication;
using PetHost.Modules.Hosts.Application.HostApplications.GetHostApplicationById;
using PetHost.Modules.Hosts.Application.HostApplications.GetHostApplicationDocument;
using PetHost.Modules.Hosts.Application.HostApplications.ListHostApplications;
using PetHost.Modules.Hosts.Application.HostApplications.RejectHostApplication;
using PetHost.Modules.Hosts.Application.HostApplications.Responses;
using PetHost.Modules.Hosts.Application.Hosts.GetMyHost;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;
using Xunit;
using static PetHost.Modules.Hosts.Application.UnitTests.HostApplications.HostsTestData;

namespace PetHost.Modules.Hosts.Application.UnitTests.HostApplications;

/// <summary>A análise do admin: fila, detalhe, fotos, aprovar e recusar.</summary>
public sealed class HostApplicationReviewTests
{
    private readonly Mock<IHostApplicationRepository> _applications = new();
    private readonly Mock<IHostRepository> _hosts = new();
    private readonly Mock<IHostsUnitOfWork> _unitOfWork = UnitOfWork();
    private readonly Mock<IPrivateImageStorage> _storage = new();
    private readonly Mock<IHostAccounts> _accounts = Accounts();
    private readonly Mock<IHostApplicationNotifier> _notifier = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Mock<IIdentityBlockRepository> _identityBlocks = new();
    private readonly FakeTimeProvider _time = new(Now.AddHours(1));
    private readonly HostApplication _application = Submitted();

    public HostApplicationReviewTests()
    {
        _applications.Setup(r => r.GetByIdAsync(_application.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_application);
        _applications.Setup(r => r.ListByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([_application]);
    }

    private ApproveHostApplicationCommandHandler Approve(TimeSpan? retention = null) =>
        new(_applications.Object, _hosts.Object, _unitOfWork.Object, _storage.Object, Policy(retention ?? TimeSpan.Zero),
            _accounts.Object, _notifier.Object, _auditTrail.Object, _time);

    private RejectHostApplicationCommandHandler Reject() =>
        new(_applications.Object, _identityBlocks.Object, Fingerprint(), _unitOfWork.Object, _storage.Object, Policy(TimeSpan.Zero),
            _accounts.Object, _notifier.Object, _auditTrail.Object, _time);

    // ----- bloqueio do CPF na recusa -----

    [Fact]
    public async Task Reject_Should_BlockTheCpf_When_TheReasonIsSuspectedFraud()
    {
        // Act
        var result = await Reject().HandleAsync(
            new RejectHostApplicationCommand(AdminId, _application.Id.Value, "suspected_fraud", "Documento adulterado"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _identityBlocks.Verify(
            b => b.Add(It.Is<IdentityBlock>(x => x.CpfFingerprint == FingerprintOf(CpfText) && x.Reason == IdentityBlockReason.Fraud && x.ApplicationId == _application.Id.Value)),
            Times.Once);
        _auditTrail.Verify(
            t => t.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.HostIdentityBlocked && r.Details!["blockReason"] == "fraud" && r.Details["cpf"] == "***.***.247-25"), It.IsAny<CancellationToken>()),
            Times.Once);
        _auditTrail.Verify(
            t => t.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.HostApplicationRejected && r.Details!["identityBlocked"] == "fraud"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Reject_Should_BlockTheCpf_When_ItIsTheLastAttempt()
    {
        // Arrange: duas recusas comuns antes desta.
        _applications.Setup(r => r.ListByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([_application, Rejected(), Rejected()]);

        // Act
        await Reject().HandleAsync(new RejectHostApplicationCommand(AdminId, _application.Id.Value, "document_illegible", "Foto borrada"), CancellationToken.None);

        // Assert
        _identityBlocks.Verify(b => b.Add(It.Is<IdentityBlock>(x => x.Reason == IdentityBlockReason.AttemptsExhausted)), Times.Once);
    }

    [Fact]
    public async Task Reject_Should_NotBlockTheCpf_When_ThePersonCanTryAgain()
    {
        // Act
        await Reject().HandleAsync(new RejectHostApplicationCommand(AdminId, _application.Id.Value, "document_illegible", "Foto borrada"), CancellationToken.None);

        // Assert
        _identityBlocks.Verify(b => b.Add(It.IsAny<IdentityBlock>()), Times.Never);
        _auditTrail.Verify(t => t.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.HostIdentityBlocked), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reject_Should_KeepTheExistingBlock_When_TheCpfIsAlreadyBlocked()
    {
        // Arrange
        _identityBlocks
            .Setup(b => b.FindAsync(FingerprintOf(CpfText), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityBlock.Create(FingerprintOf(CpfText), IdentityBlockReason.AttemptsExhausted, Guid.CreateVersion7(), Now));

        // Act
        await Reject().HandleAsync(new RejectHostApplicationCommand(AdminId, _application.Id.Value, "suspected_fraud", "Documento adulterado"), CancellationToken.None);

        // Assert
        _identityBlocks.Verify(b => b.Add(It.IsAny<IdentityBlock>()), Times.Never);
    }

    [Fact]
    public async Task Reject_Should_DropTheBlockFromTheContext_When_SavingItConflicts()
    {
        // Arrange: a recusa grava; o bloqueio esbarra no índice único (outra recusa ao mesmo tempo).
        _unitOfWork
            .SetupSequence(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success())
            .ReturnsAsync(Result.Failure(new Error("HOST_CONFLICT", "conflict")))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await Reject().HandleAsync(
            new RejectHostApplicationCommand(AdminId, _application.Id.Value, "suspected_fraud", "Documento adulterado"),
            CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue("a recusa já foi gravada; o bloqueio da conta vale");
        _identityBlocks.Verify(b => b.Remove(It.IsAny<IdentityBlock>()), Times.Once);
    }

    // ----- aprovar -----

    [Fact]
    public async Task Approve_Should_CreateTheHostRecordAndNotify_When_Submitted()
    {
        // Act
        var result = await Approve().HandleAsync(new ApproveHostApplicationCommand(AdminId, _application.Id.Value, "Documento e selfie conferidos"), CancellationToken.None);

        // Assert
        result.Value!.Status.Should().Be("approved");
        result.Value.ReviewedBy.Should().Be(AdminId);
        result.Value.User!.FullName.Should().Be("Dona Cida");
        _hosts.Verify(h => h.Add(It.Is<Host>(host => host.UserId == UserId && host.ApplicationId == _application.Id.Value && host.VerifiedBy == AdminId)), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.HostApplicationApproved && r.Reason == "Documento e selfie conferidos"),
            It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(n => n.NotifyApprovedAsync(It.Is<HostApplicationDecision>(d => d.Email == "cida@exemplo.com"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Approve_Should_DeleteThePhotos_When_RetentionIsZero()
    {
        // Act
        var result = await Approve().HandleAsync(new ApproveHostApplicationCommand(AdminId, _application.Id.Value, "ok"), CancellationToken.None);

        // Assert
        result.Value!.Documents.Should().BeEmpty();
        result.Value.DocumentsPurgedAt.Should().NotBeNull();
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task Approve_Should_KeepThePhotos_When_RetentionHasNotPassed()
    {
        // Act
        var result = await Approve(TimeSpan.FromDays(90)).HandleAsync(new ApproveHostApplicationCommand(AdminId, _application.Id.Value, "ok"), CancellationToken.None);

        // Assert
        result.Value!.Documents.Should().HaveCount(3);
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Approve_Should_ReturnNotFound_When_TheApplicationDoesNotExist()
    {
        // Act
        var result = await Approve().HandleAsync(new ApproveHostApplicationCommand(AdminId, Guid.CreateVersion7(), "ok"), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_NOT_FOUND");
    }

    [Fact]
    public async Task Approve_Should_ReturnNotUnderReview_When_AlreadyDecided()
    {
        // Arrange
        _application.Reject(AdminId, RejectionReason.Other, "x", Now);

        // Act
        var result = await Approve().HandleAsync(new ApproveHostApplicationCommand(AdminId, _application.Id.Value, "ok"), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_NOT_UNDER_REVIEW");
        _hosts.Verify(h => h.Add(It.IsAny<Host>()), Times.Never);
    }

    [Fact]
    public async Task Approve_Should_ReturnCpfAlreadyRegistered_When_AnotherHostGotTheCpf()
    {
        // Arrange
        _hosts.Setup(h => h.ExistsIndividualByCpfAsync(_application.Cpf, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var result = await Approve().HandleAsync(new ApproveHostApplicationCommand(AdminId, _application.Id.Value, "ok"), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_CPF_ALREADY_REGISTERED");
        _application.Status.Should().Be(HostApplicationStatus.Submitted);
    }

    [Fact]
    public async Task Approve_Should_NotNotifyNorRecord_When_AnotherAdminDecidedFirst()
    {
        // Arrange
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(new Error("HOST_APPLICATION_CONFLICT", "race")));

        // Act
        var result = await Approve().HandleAsync(new ApproveHostApplicationCommand(AdminId, _application.Id.Value, "ok"), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_CONFLICT");
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifier.Verify(n => n.NotifyApprovedAsync(It.IsAny<HostApplicationDecision>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("Conferido", true)]
    public void ApproveValidator_Should_RequireAReason_When_Validating(string reason, bool valid)
    {
        // Act
        var result = new ApproveHostApplicationCommandValidator().Validate(new ApproveHostApplicationCommand(AdminId, Guid.CreateVersion7(), reason));

        // Assert
        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public void ApproveValidator_Should_RefuseALongReason_When_Validating()
    {
        // Act
        var result = new ApproveHostApplicationCommandValidator()
            .Validate(new ApproveHostApplicationCommand(AdminId, Guid.CreateVersion7(), new string('x', HostApplication.ReviewNotesMaxLength + 1)));

        // Assert
        result.IsValid.Should().BeFalse();
    }

    // ----- recusar -----

    [Fact]
    public async Task Reject_Should_LetThePersonTryAgain_When_TheReasonIsCommon()
    {
        // Act
        var result = await Reject().HandleAsync(
            new RejectHostApplicationCommand(AdminId, _application.Id.Value, "selfie_mismatch", "Selfie sem o documento"),
            CancellationToken.None);

        // Assert
        result.Value!.Status.Should().Be("rejected");
        result.Value.RejectionReason.Should().Be("selfie_mismatch");
        result.Value.ReviewNotes.Should().Be("Selfie sem o documento");
        _notifier.Verify(n => n.NotifyRejectedAsync(
            It.Is<HostApplicationDecision>(d => d.RejectionReason == "selfie_mismatch" && d.CanTryAgain),
            It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.HostApplicationRejected && r.Details!["final"] == "false"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reject_Should_CloseForGood_When_FraudIsSuspected()
    {
        // Act
        var result = await Reject().HandleAsync(
            new RejectHostApplicationCommand(AdminId, _application.Id.Value, "suspected_fraud", "Documento adulterado"),
            CancellationToken.None);

        // Assert
        result.Value!.Status.Should().Be("rejected_final");
        _notifier.Verify(n => n.NotifyRejectedAsync(It.Is<HostApplicationDecision>(d => !d.CanTryAgain), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reject_Should_ReturnNotUnderReview_When_StillDraft()
    {
        // Arrange
        var draft = Draft();
        _applications.Setup(r => r.GetByIdAsync(draft.Id, It.IsAny<CancellationToken>())).ReturnsAsync(draft);

        // Act
        var result = await Reject().HandleAsync(new RejectHostApplicationCommand(AdminId, draft.Id.Value, "other", "x"), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_NOT_UNDER_REVIEW");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void RejectValidator_Should_ReturnBothFields_When_TheBodyIsWrong()
    {
        // Act
        var result = new RejectHostApplicationCommandValidator().Validate(new RejectHostApplicationCommand(AdminId, Guid.CreateVersion7(), "ugly", null));

        // Assert
        result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["RejectionReason", "Reason"]);
    }

    // ----- ver -----

    [Fact]
    public async Task GetById_Should_ReturnTheDataAndRecordTheView_When_Found()
    {
        // Act
        var result = await new GetHostApplicationByIdQueryHandler(_applications.Object, _accounts.Object, _auditTrail.Object)
            .HandleAsync(new GetHostApplicationByIdQuery(_application.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.Cpf.Should().Be("52998224725");
        result.Value.Documents.Select(d => d.Path).Should().Contain($"/api/v1/host-applications/{_application.Id.Value}/documents/selfie");
        result.Value.User!.BirthDate.Should().Be(new DateOnly(1968, 3, 15));
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.HostApplicationViewed), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_Should_ShowTheQueueAndRecordTheListing_When_NoFilterIsGiven()
    {
        // Arrange
        _applications.Setup(r => r.ListByStatusAsync(HostApplicationStatus.Submitted, It.IsAny<CancellationToken>())).ReturnsAsync([_application]);

        // Act
        var result = await new ListHostApplicationsQueryHandler(_applications.Object, _accounts.Object, _auditTrail.Object)
            .HandleAsync(new ListHostApplicationsQuery(null), CancellationToken.None);

        // Assert
        result.Value.Should().ContainSingle().Which.User!.Email.Should().Be("cida@exemplo.com");
        _accounts.Verify(a => a.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.HostApplicationsListed && r.Details!["status"] == "submitted" && r.Details["count"] == "1"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task List_Should_ReturnAValidationError_When_TheStatusIsUnknown()
    {
        // Act
        var result = await new ListHostApplicationsQueryHandler(_applications.Object, _accounts.Object, _auditTrail.Object)
            .HandleAsync(new ListHostApplicationsQuery("archived"), CancellationToken.None);

        // Assert
        result.FirstError!.Field.Should().Be("status");
    }

    [Fact]
    public async Task GetDocument_Should_ReturnASignedLinkAndRecordTheView_When_ItExists()
    {
        // Arrange
        var link = new SignedImageUrl("https://bucket.test/host-applications/selfie.jpg?X-Amz-Signature=abc", Now.AddMinutes(5));
        _storage.Setup(s => s.GetSignedUrlAsync("host-applications/selfie.jpg", It.IsAny<CancellationToken>()))
            .ReturnsAsync(link);

        // Act
        var result = await Document().HandleAsync(new GetHostApplicationDocumentQuery(_application.Id.Value, "selfie"), CancellationToken.None);

        // Assert
        result.Value.Should().Be(new HostApplicationDocumentLinkResponse("selfie", link.Url, link.ExpiresAt.ToUnixTimeSeconds()));
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.HostApplicationDocumentViewed && r.Details!["kind"] == "selfie"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetDocument_Should_ReturnNotFound_When_ThePhotoWasPurged()
    {
        // Arrange
        _application.Cancel(Now);
        _application.PurgeDocuments(Now);

        // Act
        var result = await Document().HandleAsync(new GetHostApplicationDocumentQuery(_application.Id.Value, "front"), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_DOCUMENT_NOT_FOUND");
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetDocument_Should_ReturnNotFound_When_TheFileIsGoneFromTheBucket()
    {
        // Act
        var result = await Document().HandleAsync(new GetHostApplicationDocumentQuery(_application.Id.Value, "front"), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_DOCUMENT_NOT_FOUND");
    }

    [Fact]
    public async Task GetDocument_Should_ReturnAValidationError_When_TheKindIsUnknown()
    {
        // Act
        var result = await Document().HandleAsync(new GetHostApplicationDocumentQuery(_application.Id.Value, "passport"), CancellationToken.None);

        // Assert
        result.FirstError!.Field.Should().Be("kind");
        _applications.Verify(r => r.GetByIdAsync(It.IsAny<HostApplicationId>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetMyHost_Should_ReturnNotFound_When_NotApprovedYet()
    {
        // Act
        var result = await new GetMyHostQueryHandler(_hosts.Object, _accounts.Object).HandleAsync(new GetMyHostQuery(UserId), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_NOT_FOUND");
    }

    [Fact]
    public async Task GetMyHost_Should_ReturnTheVerifiedProfile_When_Approved()
    {
        // Arrange
        _hosts.Setup(h => h.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Host.CreateIndividual(UserId, AnyCpf, _application.Id.Value, AdminId, Now));

        // Act
        var result = await new GetMyHostQueryHandler(_hosts.Object, _accounts.Object).HandleAsync(new GetMyHostQuery(UserId), CancellationToken.None);

        // Assert
        result.Value!.VerifiedAt.Should().Be(Now);
        result.Value.StripeConnected.Should().BeFalse();
        result.Value.User!.FullName.Should().Be("Dona Cida");
    }

    private GetHostApplicationDocumentQueryHandler Document() => new(_applications.Object, _storage.Object, _auditTrail.Object);
}
