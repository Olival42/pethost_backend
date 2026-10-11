using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Hosts.Application.HostApplications.CancelMyHostApplication;
using PetHost.Modules.Hosts.Application.HostApplications.SubmitMyHostApplication;
using PetHost.Modules.Hosts.Application.HostApplications.UploadHostApplicationDocuments;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;
using Xunit;
using static PetHost.Modules.Hosts.Application.UnitTests.HostApplications.HostsTestData;

namespace PetHost.Modules.Hosts.Application.UnitTests.HostApplications;

/// <summary>Enviar fotos, enviar a solicitação e desistir.</summary>
public sealed class HostApplicationDocumentsTests
{
    private readonly Mock<IHostApplicationRepository> _applications = new();
    private readonly Mock<IHostsUnitOfWork> _unitOfWork = UnitOfWork();
    private readonly Mock<IPrivateImageStorage> _storage = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly List<HostApplication> _history = [];
    private readonly List<string> _savedKeys = [];

    public HostApplicationDocumentsTests()
    {
        _applications.Setup(r => r.ListByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(() => _history);

        // Cada foto gravada ganha chave e hash próprios: new-1.jpg com "111…", new-2.jpg com "222…"...
        _storage
            .Setup(s => s.SaveAsync(It.IsAny<ImageUpload?>(), PrivateImageFolders.HostApplications, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Saved(hashChar: (char)('0' + _savedKeys.Count + 1)));
    }

    private Result<StoredPrivateImage> Saved(char hashChar)
    {
        var key = $"host-applications/new-{_savedKeys.Count + 1}.jpg";
        _savedKeys.Add(key);
        return Result<StoredPrivateImage>.Success(new StoredPrivateImage(key, new string(hashChar, 64), 321));
    }

    private UploadHostApplicationDocumentsCommandHandler Upload() =>
        new(_applications.Object, _unitOfWork.Object, _storage.Object, _auditTrail.Object, _time);

    private static UploadHostApplicationDocumentsCommand AllThree() => new(UserId, Image(), Image(), Image());

    private static UploadHostApplicationDocumentsCommand OnlyFront() => new(UserId, Image(), null, null);

    // ----- enviar fotos -----

    [Fact]
    public async Task Upload_Should_StoreTheThreePhotosInThePrivateBucket_When_AllAreSentTogether()
    {
        // Arrange
        _history.Add(Draft());

        // Act
        var result = await Upload().HandleAsync(AllThree(), CancellationToken.None);

        // Assert
        result.Value!.Documents.Select(d => d.Kind).Should().BeEquivalentTo(["front", "back", "selfie"]);
        _history[0].MissingDocuments.Should().BeEmpty();
        _history[0].Documents.Select(d => d.StorageKey).Should().BeEquivalentTo(_savedKeys);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync(It.Is<string?>(k => k != null), It.IsAny<CancellationToken>()), Times.Never);
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.HostApplicationDocumentUploaded && r.Details!["kinds"] == "front,back,selfie" && r.Details["replaced"] == "0"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Upload_Should_ReplaceOnlyTheSentSlot_When_JustOnePhotoIsSent()
    {
        // Arrange
        var application = WithAllPhotos();
        _history.Add(application);

        // Act
        var result = await Upload().HandleAsync(OnlyFront(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        application.Documents.Single(d => d.Kind == DocumentKind.Front).StorageKey.Should().Be(_savedKeys[0]);
        application.Documents.Single(d => d.Kind == DocumentKind.Back).StorageKey.Should().Be("host-applications/back.jpg");
        _storage.Verify(s => s.DeleteAsync("host-applications/front.jpg", It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync(_savedKeys[0], It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Upload_Should_NotTouchTheBucket_When_ThereIsNoOpenApplication()
    {
        // Act
        var result = await Upload().HandleAsync(AllThree(), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_NOT_FOUND");
        _storage.Verify(s => s.SaveAsync(It.IsAny<ImageUpload?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Upload_Should_NotTouchTheBucket_When_TheApplicationWasAlreadySubmitted()
    {
        // Arrange
        _history.Add(Submitted());

        // Act
        var result = await Upload().HandleAsync(AllThree(), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_NOT_EDITABLE");
        _storage.Verify(s => s.SaveAsync(It.IsAny<ImageUpload?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Upload_Should_PointTheErrorToThePartAndDropTheOthers_When_OneImageIsInvalid()
    {
        // Arrange
        _history.Add(Draft());
        _storage
            .SetupSequence(s => s.SaveAsync(It.IsAny<ImageUpload?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StoredPrivateImage>.Success(new StoredPrivateImage("host-applications/f.jpg", new string('f', 64), 1)))
            .ReturnsAsync(Result<StoredPrivateImage>.Failure(ImageErrors.UnsupportedFormat));

        // Act
        var result = await Upload().HandleAsync(AllThree(), CancellationToken.None);

        // Assert
        result.FirstError!.Field.Should().Be("back");
        result.FirstError.Message.Should().Be(ImageErrors.UnsupportedFormat.Message);
        _storage.Verify(s => s.DeleteAsync("host-applications/f.jpg", It.IsAny<CancellationToken>()), Times.Once, "tudo ou nada");
        _storage.Verify(s => s.SaveAsync(It.IsAny<ImageUpload?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2), "a selfie nem sobe");
        _history[0].Documents.Should().BeEmpty();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Upload_Should_DeleteTheNewPhotos_When_TheSameImageIsSentTwice()
    {
        // Arrange
        _history.Add(Draft());
        _storage
            .Setup(s => s.SaveAsync(It.IsAny<ImageUpload?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Saved(hashChar: 's'));

        // Act
        var result = await Upload().HandleAsync(AllThree(), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_DOCUMENT_ALREADY_EXISTS");
        _savedKeys.Should().HaveCount(3);
        foreach (var key in _savedKeys)
            _storage.Verify(s => s.DeleteAsync(key, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Upload_Should_DeleteTheNewPhoto_When_TheSameImageIsInAnotherSlot()
    {
        // Arrange
        var application = Draft();
        application.AttachDocument(DocumentKind.Back, "host-applications/back.jpg", new string('1', 64), 10, Now);
        _history.Add(application);

        // Act
        var result = await Upload().HandleAsync(OnlyFront(), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_DOCUMENT_ALREADY_EXISTS");
        _storage.Verify(s => s.DeleteAsync(_savedKeys[0], It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Upload_Should_DeleteTheNewPhotos_When_SavingFails()
    {
        // Arrange
        _history.Add(Draft());
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(new Error("HOST_APPLICATION_CONFLICT", "race")));

        // Act
        var result = await Upload().HandleAsync(AllThree(), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_CONFLICT");
        _savedKeys.Should().HaveCount(3);
        foreach (var key in _savedKeys)
            _storage.Verify(s => s.DeleteAsync(key, It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Upload_Should_DeleteTheNewPhotos_When_SavingThrows()
    {
        // Arrange
        _history.Add(Draft());
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));

        // Act
        var act = () => Upload().HandleAsync(AllThree(), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _savedKeys.Should().HaveCount(3);
        foreach (var key in _savedKeys)
            _storage.Verify(s => s.DeleteAsync(key, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void UploadValidator_Should_RequireAtLeastOnePhoto_When_NothingIsSent()
    {
        // Act
        var result = new UploadHostApplicationDocumentsCommandValidator()
            .Validate(new UploadHostApplicationDocumentsCommand(UserId, null, null, null));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "documents");
    }

    [Fact]
    public void UploadValidator_Should_Pass_When_OnlyOnePhotoIsSent()
    {
        // Act
        var result = new UploadHostApplicationDocumentsCommandValidator()
            .Validate(new UploadHostApplicationDocumentsCommand(UserId, null, null, Image()));

        // Assert
        result.IsValid.Should().BeTrue();
    }

    // ----- enviar -----

    [Fact]
    public async Task Submit_Should_SendToTheQueue_When_AllPhotosArePresent()
    {
        // Arrange
        _history.Add(WithAllPhotos());

        // Act
        var result = await new SubmitMyHostApplicationCommandHandler(_applications.Object, _unitOfWork.Object, _auditTrail.Object, _time)
            .HandleAsync(new SubmitMyHostApplicationCommand(UserId), CancellationToken.None);

        // Assert
        result.Value!.Status.Should().Be("submitted");
        result.Value.SubmittedAt.Should().Be(Now);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.HostApplicationSubmitted), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Submit_Should_ReturnMissingDocuments_When_PhotosAreMissing()
    {
        // Arrange
        _history.Add(Draft());

        // Act
        var result = await new SubmitMyHostApplicationCommandHandler(_applications.Object, _unitOfWork.Object, _auditTrail.Object, _time)
            .HandleAsync(new SubmitMyHostApplicationCommand(UserId), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_DOCUMENTS_MISSING");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ----- desistir -----

    [Fact]
    public async Task Cancel_Should_DeleteThePhotosRightAway_When_RetentionIsZero()
    {
        // Arrange
        _history.Add(WithAllPhotos());

        // Act
        var result = await Cancel(TimeSpan.Zero).HandleAsync(new CancelMyHostApplicationCommand(UserId), CancellationToken.None);

        // Assert
        result.Value!.Status.Should().Be("cancelled");
        result.Value.DocumentsPurgedAt.Should().Be(Now);
        _storage.Verify(s => s.DeleteAsync(It.Is<string?>(k => k!.StartsWith("host-applications/", StringComparison.Ordinal)), It.IsAny<CancellationToken>()), Times.Exactly(3));
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.HostApplicationCancelled), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_Should_KeepThePhotos_When_RetentionHasNotPassed()
    {
        // Arrange
        _history.Add(WithAllPhotos());

        // Act
        var result = await Cancel(TimeSpan.FromDays(30)).HandleAsync(new CancelMyHostApplicationCommand(UserId), CancellationToken.None);

        // Assert
        result.Value!.DocumentsPurgedAt.Should().BeNull();
        _storage.Verify(s => s.DeleteAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Cancel_Should_ReturnNotFound_When_NothingIsOpen()
    {
        // Arrange
        _history.Add(Rejected());

        // Act
        var result = await Cancel(TimeSpan.Zero).HandleAsync(new CancelMyHostApplicationCommand(UserId), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_APPLICATION_NOT_FOUND");
    }

    private CancelMyHostApplicationCommandHandler Cancel(TimeSpan retention) =>
        new(_applications.Object, _unitOfWork.Object, _storage.Object, Policy(retention), _auditTrail.Object, _time);
}
