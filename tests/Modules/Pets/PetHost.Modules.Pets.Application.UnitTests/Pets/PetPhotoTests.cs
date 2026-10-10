using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Pets.Application.Pets.AddPetPhoto;
using PetHost.Modules.Pets.Application.Pets.RemovePetPhoto;
using PetHost.Modules.Pets.Application.Pets.ReplacePetPhoto;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Results;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Pets.Application.UnitTests.Pets;

/// <summary>Dublês comuns aos três casos de uso de foto.</summary>
public abstract class PetPhotoHandlerTests
{
    protected const string NewUrl = "https://img.pethost.test/pets/new.png";
    protected const string NewHash = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";

    protected Mock<IPetRepository> PetRepository { get; } = new();
    protected Mock<IPetsUnitOfWork> UnitOfWork { get; } = new();
    protected Mock<IImageStorage> Storage { get; } = new();
    protected Mock<IAuditTrail> AuditTrail { get; } = new();
    protected ImageUpload Image { get; } = new(new MemoryStream([1]), "a.png", "image/png");
    protected FakeTimeProvider Time { get; } = new(PetProfileBuilder.DefaultNow.AddDays(1));

    protected void Arrange(Pet pet)
    {
        PetRepository.Setup(r => r.GetByIdAsync(pet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(pet);
        Storage
            .Setup(s => s.SaveAsync(Image, ImageFolders.Pets, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<StoredImage>.Success(new StoredImage(NewUrl, NewHash)));
    }

    protected void VerifyNothingUploaded() =>
        Storage.Verify(s => s.SaveAsync(It.IsAny<ImageUpload?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
}

public sealed class AddPetPhotoCommandHandlerTests : PetPhotoHandlerTests
{
    private readonly AddPetPhotoCommandHandler _sut;

    public AddPetPhotoCommandHandlerTests()
    {
        _sut = new AddPetPhotoCommandHandler(
            PetRepository.Object, UnitOfWork.Object, PetsTestData.Keepers().Object, Storage.Object, AuditTrail.Object, Time);
    }

    [Fact]
    public async Task HandleAsync_Should_AddThePhotoAndDeleteNothing_When_PetHasRoom()
    {
        // Arrange
        var pet = new PetProfileBuilder().BuildPet(PetsTestData.Owner, photos: 1);
        Arrange(pet);

        // Act
        var result = await _sut.HandleAsync(Command(pet, PetsTestData.OwnerUserId), CancellationToken.None);

        // Assert
        result.Value!.Photos.Select(p => p.Url).Should().Equal(PetProfileBuilder.PhotoUrl(1), NewUrl);
        UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        Storage.Verify(s => s.DeleteAsync(It.Is<string?>(u => u != null), It.IsAny<CancellationToken>()), Times.Never);
        AuditTrail.Verify(
            a => a.RecordAsync(It.Is<AuditRecord>(r => r.Details!["fields"] == "photos.added"), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnLimitReachedWithoutUploading_When_PetHasThreePhotos()
    {
        // Arrange
        var pet = new PetProfileBuilder().BuildPet(PetsTestData.Owner, photos: Pet.MaxPhotos);
        Arrange(pet);

        // Act
        var result = await _sut.HandleAsync(Command(pet, PetsTestData.OwnerUserId), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("PET_PHOTO_LIMIT_REACHED");
        VerifyNothingUploaded();
    }

    [Fact]
    public async Task HandleAsync_Should_NotUploadAnything_When_PetBelongsToAnotherAccount()
    {
        // Arrange
        var pet = new PetProfileBuilder().BuildPet(PetsTestData.Owner);
        Arrange(pet);

        // Act
        var result = await _sut.HandleAsync(Command(pet, PetsTestData.StrangerUserId), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("PET_NOT_FOUND");
        VerifyNothingUploaded();
    }

    [Fact]
    public async Task HandleAsync_Should_DeleteTheNewImage_When_SavingThePetThrows()
    {
        // Arrange
        var pet = new PetProfileBuilder().BuildPet(PetsTestData.Owner);
        Arrange(pet);
        UnitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("banco fora"));

        // Act
        var act = () => _sut.HandleAsync(Command(pet, PetsTestData.OwnerUserId), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        Storage.Verify(s => s.DeleteAsync(NewUrl, It.IsAny<CancellationToken>()), Times.Once);
    }

    private AddPetPhotoCommand Command(Pet pet, Guid userId) => new(userId, Roles.Owner, pet.Id.Value, Image);
}

public sealed class ReplacePetPhotoCommandHandlerTests : PetPhotoHandlerTests
{
    private readonly ReplacePetPhotoCommandHandler _sut;

    public ReplacePetPhotoCommandHandlerTests()
    {
        _sut = new ReplacePetPhotoCommandHandler(
            PetRepository.Object, UnitOfWork.Object, PetsTestData.Keepers().Object, Storage.Object, AuditTrail.Object, Time);
    }

    [Fact]
    public async Task HandleAsync_Should_SwapTheImageKeepingTheIdAndDeleteTheOldOne_When_PhotoIsFromThePet()
    {
        // Arrange
        var pet = new PetProfileBuilder().BuildPet(PetsTestData.Owner, photos: 2);
        Arrange(pet);
        var photo = pet.Photos[1];

        // Act
        var result = await _sut.HandleAsync(
            new ReplacePetPhotoCommand(PetsTestData.OwnerUserId, Roles.Owner, pet.Id.Value, photo.Id.Value, Image),
            CancellationToken.None);

        // Assert
        result.Value!.Photos.Should().HaveCount(2);
        result.Value.Photos[1].Id.Should().Be(photo.Id.Value);
        result.Value.Photos[1].Url.Should().Be(NewUrl);
        Storage.Verify(s => s.DeleteAsync(PetProfileBuilder.PhotoUrl(2), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnPhotoNotFoundWithoutUploading_When_PhotoIsNotFromThePet()
    {
        // Arrange
        var pet = new PetProfileBuilder().BuildPet(PetsTestData.Owner, photos: 1);
        Arrange(pet);

        // Act
        var result = await _sut.HandleAsync(
            new ReplacePetPhotoCommand(PetsTestData.OwnerUserId, Roles.Owner, pet.Id.Value, Guid.CreateVersion7(), Image),
            CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("PET_PHOTO_NOT_FOUND");
        VerifyNothingUploaded();
    }
}

public sealed class RemovePetPhotoCommandHandlerTests : PetPhotoHandlerTests
{
    private readonly RemovePetPhotoCommandHandler _sut;

    public RemovePetPhotoCommandHandlerTests()
    {
        _sut = new RemovePetPhotoCommandHandler(
            PetRepository.Object, UnitOfWork.Object, PetsTestData.Keepers().Object, Storage.Object, AuditTrail.Object, Time);
    }

    [Fact]
    public async Task HandleAsync_Should_RemoveThePhotoAndDeleteTheImage_When_PhotoIsFromThePet()
    {
        // Arrange
        var pet = new PetProfileBuilder().BuildPet(PetsTestData.Owner, photos: 2);
        Arrange(pet);

        // Act
        var result = await _sut.HandleAsync(
            new RemovePetPhotoCommand(PetsTestData.OwnerUserId, Roles.Owner, pet.Id.Value, pet.Photos[0].Id.Value),
            CancellationToken.None);

        // Assert
        result.Value!.Photos.Select(p => p.Url).Should().Equal(PetProfileBuilder.PhotoUrl(2));
        Storage.Verify(s => s.DeleteAsync(PetProfileBuilder.PhotoUrl(1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnPhotoNotFoundAndDeleteNothing_When_PhotoIsNotFromThePet()
    {
        // Arrange
        var pet = new PetProfileBuilder().BuildPet(PetsTestData.Owner, photos: 1);
        Arrange(pet);

        // Act
        var result = await _sut.HandleAsync(
            new RemovePetPhotoCommand(PetsTestData.OwnerUserId, Roles.Owner, pet.Id.Value, Guid.CreateVersion7()),
            CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("PET_PHOTO_NOT_FOUND");
        UnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        Storage.Verify(s => s.DeleteAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
