using FluentAssertions;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using Xunit;

namespace PetHost.Modules.Hosts.Domain.UnitTests.HostApplications;

public sealed class HostApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddHours(2);
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid AdminId = Guid.CreateVersion7();

    private static Cpf AnyCpf => Cpf.Create("52998224725").Value!;
    private static Cpf OtherCpf => Cpf.Create("11144477735").Value!;

    // ----- abrir -----

    [Fact]
    public void Start_Should_OpenADraftAsFirstAttempt_When_AccountHasNoHistory()
    {
        // Act
        var result = HostApplication.Start([], UserId, AnyCpf, IdentityDocumentType.Cnh, null, "2026-10-10", " 203.0.113.7 ", Now);

        // Assert
        var application = result.Value!;
        application.Status.Should().Be(HostApplicationStatus.Draft);
        application.UserId.Should().Be(UserId);
        application.PersonType.Should().Be(PersonType.Individual);
        application.Cpf.Should().Be(AnyCpf);
        application.DocumentType.Should().Be(IdentityDocumentType.Cnh);
        application.AttemptNumber.Should().Be(1);
        application.NoticeVersion.Should().Be("2026-10-10");
        application.NoticeAcceptedAt.Should().Be(Now);
        application.NoticeIpAddress.Should().Be("203.0.113.7");
        application.Documents.Should().BeEmpty();
        application.MissingDocuments.Should().Equal(DocumentKind.Front, DocumentKind.Back, DocumentKind.Selfie);
        application.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void Start_Should_CountPreviousRejections_When_AccountWasRejectedBefore()
    {
        // Arrange
        var history = new[] { Rejected(), Rejected() };

        // Act
        var result = HostApplication.Start(history, UserId, AnyCpf, IdentityDocumentType.Rg, null, "v1", null, Now);

        // Assert
        result.Value!.AttemptNumber.Should().Be(3);
        result.Value.NoticeIpAddress.Should().BeNull();
    }

    [Fact]
    public void Start_Should_IgnoreCancelledAndExpired_When_CountingAttempts()
    {
        // Arrange
        var history = new[] { Cancelled(), Expired() };

        // Act
        var result = HostApplication.Start(history, UserId, AnyCpf, IdentityDocumentType.Rg, null, "v1", null, Now);

        // Assert
        result.Value!.AttemptNumber.Should().Be(1);
    }

    [Fact]
    public void Start_Should_ReturnAlreadyOpen_When_ThereIsAnOpenApplication()
    {
        // Act
        var result = HostApplication.Start([Draft()], UserId, AnyCpf, IdentityDocumentType.Rg, null, "v1", null, Now);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_ALREADY_EXISTS");
    }

    [Fact]
    public void Start_Should_ReturnAlreadyVerified_When_AnApplicationWasApproved()
    {
        // Act
        var result = HostApplication.Start([Approved()], UserId, AnyCpf, IdentityDocumentType.Rg, null, "v1", null, Now);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_ALREADY_VERIFIED");
    }

    [Fact]
    public void Start_Should_ReturnBlocked_When_AnApplicationWasRejectedForFraud()
    {
        // Act
        var result = HostApplication.Start([Rejected(RejectionReason.SuspectedFraud)], UserId, AnyCpf, IdentityDocumentType.Rg, null, "v1", null, Now);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_BLOCKED");
    }

    [Fact]
    public void Start_Should_ReturnAttemptsExceeded_When_MaxRejectionsWereReached()
    {
        // Arrange
        var history = new[] { Rejected(), Rejected(), Rejected() };

        // Act
        var result = HostApplication.Start(history, UserId, AnyCpf, IdentityDocumentType.Rg, null, "v1", null, Now);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_ATTEMPTS_EXCEEDED");
    }

    [Fact]
    public void Start_Should_TruncateTheIpAddress_When_ItIsTooLong()
    {
        // Act
        var result = HostApplication.Start([], UserId, AnyCpf, IdentityDocumentType.Rg, null, "v1", new string('1', 60), Now);

        // Assert
        result.Value!.NoticeIpAddress.Should().HaveLength(HostApplication.IpAddressMaxLength);
    }

    [Fact]
    public void Start_Should_Throw_When_AccountIsEmpty()
    {
        // Act
        var act = () => HostApplication.Start([], Guid.Empty, AnyCpf, IdentityDocumentType.Rg, null, "v1", null, Now);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0, true, 3)]
    [InlineData(1, true, 2)]
    [InlineData(3, false, 0)]
    public void CanStartAndRemainingAttempts_Should_FollowTheRejections_When_Asked(int rejections, bool canStart, int remaining)
    {
        // Arrange
        var history = Enumerable.Range(0, rejections).Select(_ => Rejected()).ToList();

        // Act / Assert
        HostApplication.CanStart(history).Should().Be(canStart);
        HostApplication.RemainingAttempts(history).Should().Be(remaining);
    }

    [Fact]
    public void RemainingAttempts_Should_BeZero_When_AccountIsBlockedOrApproved()
    {
        // Act / Assert
        HostApplication.RemainingAttempts([Rejected(RejectionReason.SuspectedFraud)]).Should().Be(0);
        HostApplication.RemainingAttempts([Approved()]).Should().Be(0);
    }

    // ----- rascunho -----

    [Fact]
    public void ChangeDetails_Should_UpdateCpfAndDocumentType_When_Draft()
    {
        // Arrange
        var application = Draft();

        // Act
        var result = application.ChangeDetails(OtherCpf, IdentityDocumentType.Cin, null, Later);

        // Assert
        result.IsSuccess.Should().BeTrue();
        application.Cpf.Should().Be(OtherCpf);
        application.DocumentType.Should().Be(IdentityDocumentType.Cin);
        application.UpdatedAt.Should().Be(Later);
    }

    [Fact]
    public void ChangeDetails_Should_NotTouchUpdatedAt_When_NothingChanges()
    {
        // Arrange
        var application = Draft();

        // Act
        application.ChangeDetails(AnyCpf, null, null, Later);

        // Assert
        application.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void ChangeDetails_Should_ReturnNotEditable_When_AlreadySubmitted()
    {
        // Arrange
        var application = Submitted();

        // Act
        var result = application.ChangeDetails(OtherCpf, null, null, Later);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_NOT_EDITABLE");
        application.Cpf.Should().Be(AnyCpf);
    }

    [Fact]
    public void AttachDocument_Should_FillTheSlot_When_ItWasEmpty()
    {
        // Arrange
        var application = Draft();

        // Act
        var result = application.AttachDocument(DocumentKind.Front, "host-applications/a.jpg", Hash('a'), 1234, Later);

        // Assert
        result.Value.Should().BeNull("a vaga estava vazia");
        application.Documents.Should().ContainSingle(d =>
            d.Kind == DocumentKind.Front && d.StorageKey == "host-applications/a.jpg" && d.SizeBytes == 1234 && d.UploadedAt == Later);
        application.MissingDocuments.Should().Equal(DocumentKind.Back, DocumentKind.Selfie);
    }

    [Fact]
    public void AttachDocument_Should_ReturnThePreviousKey_When_TheSlotIsReplaced()
    {
        // Arrange
        var application = Draft();
        application.AttachDocument(DocumentKind.Front, "host-applications/old.jpg", Hash('a'), 10, Now);

        // Act
        var result = application.AttachDocument(DocumentKind.Front, "host-applications/new.jpg", Hash('b'), 20, Later);

        // Assert
        result.Value.Should().Be("host-applications/old.jpg");
        application.Documents.Should().ContainSingle().Which.StorageKey.Should().Be("host-applications/new.jpg");
    }

    [Fact]
    public void AttachDocument_Should_RejectTheSameImage_When_ItIsAlreadyInAnotherSlot()
    {
        // Arrange
        var application = Draft();
        application.AttachDocument(DocumentKind.Front, "host-applications/a.jpg", Hash('a'), 10, Now);

        // Act
        var result = application.AttachDocument(DocumentKind.Back, "host-applications/b.jpg", Hash('a'), 10, Later);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_DOCUMENT_ALREADY_EXISTS");
        application.Documents.Should().ContainSingle();
    }

    [Fact]
    public void AttachDocuments_Should_FillTheThreeSlots_When_SentTogether()
    {
        // Arrange
        var application = Draft();

        // Act
        var result = application.AttachDocuments(
            [
                new(DocumentKind.Front, "k/f", Hash('f'), 1),
                new(DocumentKind.Back, "k/b", Hash('b'), 1),
                new(DocumentKind.Selfie, "k/s", Hash('s'), 1),
            ],
            Later);

        // Assert
        result.Value.Should().BeEmpty();
        application.MissingDocuments.Should().BeEmpty();
        application.UpdatedAt.Should().Be(Later);
    }

    [Fact]
    public void AttachDocuments_Should_RejectTheWholeBatch_When_TwoPhotosAreTheSameImage()
    {
        // Arrange
        var application = Draft();

        // Act
        var result = application.AttachDocuments(
            [new(DocumentKind.Front, "k/f", Hash('x'), 1), new(DocumentKind.Back, "k/b", Hash('x'), 1)],
            Later);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_DOCUMENT_ALREADY_EXISTS");
        application.Documents.Should().BeEmpty();
    }

    [Fact]
    public void AttachDocuments_Should_AllowSwappingFrontAndBack_When_BothAreReplaced()
    {
        // Arrange
        var application = Draft();
        application.AttachDocument(DocumentKind.Front, "k/f1", Hash('f'), 1, Now);
        application.AttachDocument(DocumentKind.Back, "k/b1", Hash('b'), 1, Now);

        // Act
        var result = application.AttachDocuments(
            [new(DocumentKind.Front, "k/f2", Hash('b'), 1), new(DocumentKind.Back, "k/b2", Hash('f'), 1)],
            Later);

        // Assert
        result.Value.Should().BeEquivalentTo(["k/f1", "k/b1"]);
    }

    [Fact]
    public void AttachDocuments_Should_RejectAnImageAlreadyInAKeptSlot_When_OnlySomeAreSent()
    {
        // Arrange
        var application = Draft();
        application.AttachDocument(DocumentKind.Selfie, "k/s", Hash('s'), 1, Now);

        // Act
        var result = application.AttachDocuments([new(DocumentKind.Front, "k/f", Hash('s'), 1)], Later);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_DOCUMENT_ALREADY_EXISTS");
    }

    [Fact]
    public void AttachDocument_Should_ReturnNotEditable_When_NotDraft()
    {
        // Arrange
        var application = Submitted();

        // Act
        var result = application.AttachDocument(DocumentKind.Front, "host-applications/x.jpg", Hash('x'), 10, Later);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_NOT_EDITABLE");
        application.EnsureAcceptsDocuments().IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Submit_Should_SendToTheQueue_When_AllPhotosArePresent()
    {
        // Arrange
        var application = WithAllPhotos();

        // Act
        var result = application.Submit(Later);

        // Assert
        result.IsSuccess.Should().BeTrue();
        application.Status.Should().Be(HostApplicationStatus.Submitted);
        application.SubmittedAt.Should().Be(Later);
        application.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void Submit_Should_ListTheMissingPhotos_When_SomeAreMissing()
    {
        // Arrange
        var application = Draft();
        application.AttachDocument(DocumentKind.Front, "host-applications/a.jpg", Hash('a'), 10, Now);

        // Act
        var result = application.Submit(Later);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_DOCUMENTS_MISSING");
        result.FirstError.Message.Should().Contain("back, selfie");
        application.Status.Should().Be(HostApplicationStatus.Draft);
    }

    [Fact]
    public void Submit_Should_ReturnNotEditable_When_AlreadySubmitted()
    {
        // Act
        var result = Submitted().Submit(Later);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_NOT_EDITABLE");
    }

    // ----- decisão do admin -----

    [Fact]
    public void Approve_Should_CreateTheVerifiedHost_When_Submitted()
    {
        // Arrange
        var application = Submitted();

        // Act
        var result = application.Approve(AdminId, " Documento e selfie conferidos ", Later);

        // Assert
        var host = result.Value!;
        host.UserId.Should().Be(UserId);
        host.Cpf.Should().Be(AnyCpf);
        host.PersonType.Should().Be(PersonType.Individual);
        host.ApplicationId.Should().Be(application.Id.Value);
        host.VerifiedBy.Should().Be(AdminId);
        host.VerifiedAt.Should().Be(Later);

        application.Status.Should().Be(HostApplicationStatus.Approved);
        application.ReviewedBy.Should().Be(AdminId);
        application.ReviewedAt.Should().Be(Later);
        application.ClosedAt.Should().Be(Later);
        application.ReviewNotes.Should().Be("Documento e selfie conferidos");
        application.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Approve_Should_ReturnNotUnderReview_When_StillDraft()
    {
        // Act
        var result = Draft().Approve(AdminId, "ok", Later);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_NOT_UNDER_REVIEW");
    }

    [Theory]
    [InlineData(RejectionReason.DocumentIllegible, HostApplicationStatus.Rejected)]
    [InlineData(RejectionReason.SelfieMismatch, HostApplicationStatus.Rejected)]
    [InlineData(RejectionReason.SuspectedFraud, HostApplicationStatus.RejectedFinal)]
    public void Reject_Should_CloseWithTheRightStatus_When_Submitted(RejectionReason reason, HostApplicationStatus expected)
    {
        // Arrange
        var application = Submitted();

        // Act
        var result = application.Reject(AdminId, reason, "foto escura", Later);

        // Assert
        result.IsSuccess.Should().BeTrue();
        application.Status.Should().Be(expected);
        application.RejectionReason.Should().Be(reason);
        application.ReviewNotes.Should().Be("foto escura");
        application.ReviewedBy.Should().Be(AdminId);
        application.ClosedAt.Should().Be(Later);
    }

    [Fact]
    public void Reject_Should_ReturnNotUnderReview_When_AlreadyApproved()
    {
        // Arrange
        var application = Submitted();
        application.Approve(AdminId, "ok", Now);

        // Act
        var result = application.Reject(AdminId, RejectionReason.Other, "x", Later);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_NOT_UNDER_REVIEW");
        application.Status.Should().Be(HostApplicationStatus.Approved);
    }

    // ----- desistir, expirar, apagar fotos -----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancel_Should_CloseTheApplication_When_Open(bool submitted)
    {
        // Arrange
        var application = submitted ? Submitted() : Draft();

        // Act
        var result = application.Cancel(Later);

        // Assert
        result.IsSuccess.Should().BeTrue();
        application.Status.Should().Be(HostApplicationStatus.Cancelled);
        application.ClosedAt.Should().Be(Later);
    }

    [Fact]
    public void Cancel_Should_ReturnClosed_When_AlreadyDecided()
    {
        // Act
        var result = Rejected().Cancel(Later);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_CLOSED");
    }

    [Fact]
    public void Expire_Should_CloseADraft_When_Called()
    {
        // Arrange
        var application = Draft();

        // Act
        var expired = application.Expire(Later);

        // Assert
        expired.Should().BeTrue();
        application.Status.Should().Be(HostApplicationStatus.Expired);
        application.ClosedAt.Should().Be(Later);
    }

    [Fact]
    public void Expire_Should_DoNothing_When_NotDraft()
    {
        // Arrange
        var application = Submitted();

        // Act
        var expired = application.Expire(Later);

        // Assert
        expired.Should().BeFalse();
        application.Status.Should().Be(HostApplicationStatus.Submitted);
    }

    [Fact]
    public void PurgeDocuments_Should_ReturnTheKeysAndClearThePhotos_When_Closed()
    {
        // Arrange
        var application = WithAllPhotos();
        application.Cancel(Now);

        // Act
        var keys = application.PurgeDocuments(Later);

        // Assert
        keys.Should().BeEquivalentTo(["host-applications/front.jpg", "host-applications/back.jpg", "host-applications/selfie.jpg"]);
        application.Documents.Should().BeEmpty();
        application.DocumentsPurgedAt.Should().Be(Later);
    }

    [Fact]
    public void PurgeDocuments_Should_KeepThePhotos_When_StillOpen()
    {
        // Arrange
        var application = WithAllPhotos();

        // Act
        var keys = application.PurgeDocuments(Later);

        // Assert
        keys.Should().BeEmpty();
        application.Documents.Should().HaveCount(3);
        application.DocumentsPurgedAt.Should().BeNull();
    }

    [Fact]
    public void PurgeDocuments_Should_DoNothing_When_ThereAreNoPhotos()
    {
        // Arrange
        var application = Cancelled();

        // Act
        var keys = application.PurgeDocuments(Later);

        // Assert
        keys.Should().BeEmpty();
        application.DocumentsPurgedAt.Should().BeNull();
    }

    // ----- pessoa jurídica -----

    private static CompanyDetails AnyCompany(string tradeName = "Pet Feliz") =>
        CompanyDetails.Create("11222333000181", "Pet Feliz Hospedagem LTDA", tradeName, "87013000", "Av. Brasil", "1500", null, "Centro", "Maringá", "PR").Value!;

    [Fact]
    public void Start_Should_OpenACompanyApplication_When_CompanyDataIsGiven()
    {
        // Act
        var application = HostApplication.Start([], UserId, AnyCpf, IdentityDocumentType.Cnh, AnyCompany(), "v1", null, Now).Value!;

        // Assert
        application.PersonType.Should().Be(PersonType.Company);
        application.Cpf.Should().Be(AnyCpf, "na empresa, o CPF é do representante");
        application.Company.Should().Be(AnyCompany());
        application.Cnpj!.Value.Should().Be("11222333000181");
    }

    [Fact]
    public void Company_Should_BeNull_When_TheApplicationIsIndividual()
    {
        // Act / Assert
        Draft().Company.Should().BeNull();
    }

    [Fact]
    public void Approve_Should_CreateACompanyHost_When_TheApplicationIsACompany()
    {
        // Arrange
        var application = HostApplication.Start([], UserId, AnyCpf, IdentityDocumentType.Cnh, AnyCompany(), "v1", null, Now).Value!;
        application.AttachDocument(DocumentKind.Front, "k/f", Hash('f'), 1, Now);
        application.AttachDocument(DocumentKind.Back, "k/b", Hash('b'), 1, Now);
        application.AttachDocument(DocumentKind.Selfie, "k/s", Hash('s'), 1, Now);
        application.Submit(Now);

        // Act
        var host = application.Approve(AdminId, "CNPJ ativo e representante no QSA", Later).Value!;

        // Assert
        host.PersonType.Should().Be(PersonType.Company);
        host.Cpf.Should().Be(AnyCpf);
        host.Cnpj!.Value.Should().Be("11222333000181");
        host.TradeName.Should().Be("Pet Feliz");
        host.Company.Should().Be(AnyCompany());
        application.Status.Should().Be(HostApplicationStatus.Approved);
    }

    [Fact]
    public void ChangeDetails_Should_ReplaceTheCompanyData_When_TheApplicationIsACompany()
    {
        // Arrange
        var application = HostApplication.Start([], UserId, AnyCpf, IdentityDocumentType.Cnh, AnyCompany(), "v1", null, Now).Value!;

        // Act
        var result = application.ChangeDetails(null, null, AnyCompany("Cantinho Pet Feliz"), Later);

        // Assert
        result.IsSuccess.Should().BeTrue();
        application.TradeName.Should().Be("Cantinho Pet Feliz");
        application.UpdatedAt.Should().Be(Later);
    }

    [Fact]
    public void ChangeDetails_Should_RefuseCompanyData_When_TheApplicationIsIndividual()
    {
        // Arrange
        var application = Draft();

        // Act
        var result = application.ChangeDetails(null, null, AnyCompany(), Later);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_COMPANY_DATA_NOT_ALLOWED");
        application.Cnpj.Should().BeNull();
    }

    // ----- montagem -----

    private static string Hash(char c) => new(c, 64);

    private static HostApplication Draft() =>
        HostApplication.Start([], UserId, AnyCpf, IdentityDocumentType.Cnh, null, "v1", null, Now).Value!;

    private static HostApplication WithAllPhotos()
    {
        var application = Draft();
        application.AttachDocument(DocumentKind.Front, "host-applications/front.jpg", Hash('f'), 10, Now);
        application.AttachDocument(DocumentKind.Back, "host-applications/back.jpg", Hash('b'), 10, Now);
        application.AttachDocument(DocumentKind.Selfie, "host-applications/selfie.jpg", Hash('s'), 10, Now);
        return application;
    }

    private static HostApplication Submitted()
    {
        var application = WithAllPhotos();
        application.Submit(Now);
        return application;
    }

    private static HostApplication Approved()
    {
        var application = Submitted();
        application.Approve(AdminId, "ok", Now);
        return application;
    }

    private static HostApplication Rejected(RejectionReason reason = RejectionReason.DocumentIllegible)
    {
        var application = Submitted();
        application.Reject(AdminId, reason, "x", Now);
        return application;
    }

    private static HostApplication Cancelled()
    {
        var application = Draft();
        application.Cancel(Now);
        return application;
    }

    private static HostApplication Expired()
    {
        var application = Draft();
        application.Expire(Now);
        return application;
    }
}
