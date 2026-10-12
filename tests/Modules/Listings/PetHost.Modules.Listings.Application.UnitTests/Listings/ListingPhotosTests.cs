using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Listings.Application.Listings.AddListingPhoto;
using PetHost.Modules.Listings.Application.Listings.RemoveListingPhoto;
using PetHost.Modules.Listings.Application.Listings.ReorderListingPhotos;
using PetHost.Modules.Listings.Application.Listings.ReplaceListingPhoto;
using PetHost.Modules.Listings.Domain.Errors;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Listings.Application.UnitTests.Listings;

public sealed class ListingPhotosTests
{
    private static readonly DateTimeOffset Later = ListingBuilder.DefaultNow.AddDays(1);

    private readonly Mock<IListingRepository> _repository = new();
    private readonly Mock<IListingsUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Mock<IImageStorage> _storage = new();
    private readonly FakeTimeProvider _time = new(Later);
    private readonly Listing _listing = ListingsTestData.IndividualListing();
    private readonly ImageUpload _image = new(new MemoryStream([1, 2, 3]), "foto.jpg", "image/jpeg");

    public ListingPhotosTests()
    {
        _repository.Setup(r => r.GetByIdAsync(_listing.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_listing);
        _storage
            .Setup(s => s.SaveAsync(It.IsAny<ImageUpload?>(), ImageFolders.Listings, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StoredImage>.Success(new StoredImage("https://cdn.pethost.test/listings/new.jpg", new string('n', 64))));
    }

    [Fact]
    public async Task Add_Should_UploadToTheListingsFolderAndAppend_When_ThereIsRoom()
    {
        // Arrange
        var sut = new AddListingPhotoCommandHandler(_repository.Object, _unitOfWork.Object, ListingsTestData.Hosts().Object, _storage.Object, _auditTrail.Object, _time);

        // Act
        var result = await sut.HandleAsync(new AddListingPhotoCommand(ListingsTestData.IndividualUserId, _listing.Id.Value, _image), CancellationToken.None);

        // Assert
        result.Value!.Photos.Should().ContainSingle().Which.Position.Should().Be(1);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Add_Should_NotUpload_When_ListingHasFivePhotos()
    {
        // Arrange
        AddPhotos(5);
        var sut = new AddListingPhotoCommandHandler(_repository.Object, _unitOfWork.Object, ListingsTestData.Hosts().Object, _storage.Object, _auditTrail.Object, _time);

        // Act
        var result = await sut.HandleAsync(new AddListingPhotoCommand(ListingsTestData.IndividualUserId, _listing.Id.Value, _image), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.PhotoLimitReached);
        _storage.Verify(s => s.SaveAsync(It.IsAny<ImageUpload?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Add_Should_NotUpload_When_ListingIsFromAnotherHost()
    {
        // Arrange
        var sut = new AddListingPhotoCommandHandler(_repository.Object, _unitOfWork.Object, ListingsTestData.Hosts().Object, _storage.Object, _auditTrail.Object, _time);

        // Act
        var result = await sut.HandleAsync(new AddListingPhotoCommand(ListingsTestData.CompanyUserId, _listing.Id.Value, _image), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.NotFound(_listing.Id));
        _storage.Verify(s => s.SaveAsync(It.IsAny<ImageUpload?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Replace_Should_ReturnPhotoNotFoundWithoutUploading_When_PhotoIsNotFromTheListing()
    {
        // Arrange
        var sut = new ReplaceListingPhotoCommandHandler(_repository.Object, _unitOfWork.Object, ListingsTestData.Hosts().Object, _storage.Object, _auditTrail.Object, _time);
        var unknown = Guid.CreateVersion7();

        // Act
        var result = await sut.HandleAsync(new ReplaceListingPhotoCommand(ListingsTestData.IndividualUserId, _listing.Id.Value, unknown, _image), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.PhotoNotFound(new ListingPhotoId(unknown)));
        _storage.Verify(s => s.SaveAsync(It.IsAny<ImageUpload?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Replace_Should_DeleteTheOldImage_When_ImageIsReplaced()
    {
        // Arrange
        AddPhotos(1);
        var sut = new ReplaceListingPhotoCommandHandler(_repository.Object, _unitOfWork.Object, ListingsTestData.Hosts().Object, _storage.Object, _auditTrail.Object, _time);

        // Act
        var result = await sut.HandleAsync(
            new ReplaceListingPhotoCommand(ListingsTestData.IndividualUserId, _listing.Id.Value, _listing.Photos[0].Id.Value, _image),
            CancellationToken.None);

        // Assert
        result.Value!.Photos[0].Url.Should().EndWith("new.jpg");
        _storage.Verify(s => s.DeleteAsync("https://cdn.pethost.test/1.jpg", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Remove_Should_DeleteTheImageAndMoveTheOthersUp()
    {
        // Arrange
        AddPhotos(2);
        var sut = new RemoveListingPhotoCommandHandler(_repository.Object, _unitOfWork.Object, ListingsTestData.Hosts().Object, _storage.Object, _auditTrail.Object, _time);

        // Act
        var result = await sut.HandleAsync(
            new RemoveListingPhotoCommand(ListingsTestData.IndividualUserId, _listing.Id.Value, _listing.Photos[0].Id.Value),
            CancellationToken.None);

        // Assert
        result.Value!.Photos.Should().ContainSingle().Which.Should().Match<Application.Listings.Responses.ListingPhotoResponse>(p => p.Position == 1 && p.Url.EndsWith("2.jpg"));
        _storage.Verify(s => s.DeleteAsync("https://cdn.pethost.test/1.jpg", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reorder_Should_SaveTheNewOrderAndRecordIt()
    {
        // Arrange
        AddPhotos(2);
        var (first, second) = (_listing.Photos[0].Id.Value, _listing.Photos[1].Id.Value);
        var sut = new ReorderListingPhotosCommandHandler(_repository.Object, _unitOfWork.Object, ListingsTestData.Hosts().Object, _auditTrail.Object, _time);

        // Act
        var result = await sut.HandleAsync(new ReorderListingPhotosCommand(ListingsTestData.IndividualUserId, _listing.Id.Value, [second, first]), CancellationToken.None);

        // Assert
        result.Value!.Photos.Select(p => p.Id).Should().Equal(second, first);
        _auditTrail.Verify(a => a.RecordAsync(It.Is<AuditRecord>(r => r.Details!["fields"] == "photos.reordered"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Reorder_Should_ReturnOrderInvalid_When_APhotoIsMissing()
    {
        // Arrange
        AddPhotos(2);
        var sut = new ReorderListingPhotosCommandHandler(_repository.Object, _unitOfWork.Object, ListingsTestData.Hosts().Object, _auditTrail.Object, _time);

        // Act
        var result = await sut.HandleAsync(new ReorderListingPhotosCommand(ListingsTestData.IndividualUserId, _listing.Id.Value, [_listing.Photos[0].Id.Value]), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.PhotoOrderInvalid);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Reorder_Should_ReturnPhotoNotFound_When_AnIdIsNotAPhotoOfTheListing()
    {
        // Arrange
        AddPhotos(1);
        var unknown = Guid.CreateVersion7();
        var sut = new ReorderListingPhotosCommandHandler(_repository.Object, _unitOfWork.Object, ListingsTestData.Hosts().Object, _auditTrail.Object, _time);

        // Act
        var result = await sut.HandleAsync(new ReorderListingPhotosCommand(ListingsTestData.IndividualUserId, _listing.Id.Value, [unknown]), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(ListingsErrors.PhotoNotFound(new ListingPhotoId(unknown)));
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void ReorderValidator_Should_RejectRepeatedOrEmptyIds()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        var validator = new ReorderListingPhotosCommandValidator();

        // Act
        var repeated = validator.Validate(new ReorderListingPhotosCommand(ListingsTestData.IndividualUserId, Guid.CreateVersion7(), [id, id]));
        var missing = validator.Validate(new ReorderListingPhotosCommand(ListingsTestData.IndividualUserId, Guid.CreateVersion7(), null));
        var valid = validator.Validate(new ReorderListingPhotosCommand(ListingsTestData.IndividualUserId, Guid.CreateVersion7(), [id]));

        // Assert
        repeated.IsValid.Should().BeFalse();
        missing.IsValid.Should().BeFalse();
        valid.IsValid.Should().BeTrue();
    }

    private void AddPhotos(int count)
    {
        foreach (var index in Enumerable.Range(1, count))
            _listing.AddPhoto($"https://cdn.pethost.test/{index}.jpg", new string((char)('0' + index), 64), ListingBuilder.DefaultNow);
    }
}
