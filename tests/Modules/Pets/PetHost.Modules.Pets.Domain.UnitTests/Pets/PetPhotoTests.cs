using FluentAssertions;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Pets.Domain.UnitTests.Pets;

public sealed class PetPhotoTests
{
    private static readonly DateTimeOffset Now = PetProfileBuilder.DefaultNow;
    private static readonly DateTimeOffset Later = Now.AddDays(1);
    private static readonly PetKeeper Owner = PetKeeper.Owner(Guid.CreateVersion7());

    [Fact]
    public void AddPhoto_Should_FillTheSlotsInOrder_When_PetHasRoom()
    {
        // Arrange
        var pet = new PetProfileBuilder().BuildPet(Owner);

        // Act
        var first = pet.AddPhoto("https://img.pethost.test/pets/a.png", Hash("a"), Later);
        var second = pet.AddPhoto("https://img.pethost.test/pets/b.png", Hash("b"), Later);

        // Assert
        first.Value!.Position.Should().Be(1, "a primeira foto é a capa");
        second.Value!.Position.Should().Be(2);
        first.Value.PetId.Should().Be(pet.Id);
        pet.Photos.Select(p => p.Url).Should().Equal("https://img.pethost.test/pets/a.png", "https://img.pethost.test/pets/b.png");
        pet.UpdatedAt.Should().Be(Later);
    }

    [Fact]
    public void AddPhoto_Should_Fail_When_PetAlreadyHasThreePhotos()
    {
        // Arrange
        var pet = new PetProfileBuilder().BuildPet(Owner, photos: Pet.MaxPhotos);

        // Act
        var result = pet.AddPhoto("https://img.pethost.test/pets/quarta.png", Hash("q"), Later);

        // Assert
        result.FirstError!.Code.Should().Be("PET_PHOTO_LIMIT_REACHED");
        pet.CanAddPhoto.Should().BeFalse();
        pet.Photos.Should().HaveCount(Pet.MaxPhotos);
        pet.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void AddPhoto_Should_TakeTheFreedSlot_When_APhotoWasRemoved()
    {
        // Arrange
        var pet = new PetProfileBuilder().BuildPet(Owner, photos: Pet.MaxPhotos);
        pet.RemovePhoto(pet.Photos[0].Id, Later);

        // Act
        var result = pet.AddPhoto("https://img.pethost.test/pets/nova.png", Hash("n"), Later);

        // Assert
        result.Value!.Position.Should().Be(1, "a vaga liberada (a da capa) é a primeira a ser ocupada");
        pet.Photos[0].Url.Should().Be("https://img.pethost.test/pets/nova.png");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("/fotos/pipoca.png")]
    [InlineData("ftp://cdn.pethost.com/pipoca.png")]
    [InlineData("")]
    public void AddPhoto_Should_RejectTheUrl_When_ItIsNotAWebAddress(string url)
    {
        // Arrange
        var pet = new PetProfileBuilder().BuildPet(Owner);

        // Act
        var result = pet.AddPhoto(url, Hash("u"), Later);

        // Assert
        result.FirstError!.Field.Should().Be("photoUrl");
        pet.Photos.Should().BeEmpty();
    }

    [Fact]
    public void AddPhoto_Should_RejectTheUrl_When_ItIsTooLong()
    {
        // Arrange
        var pet = new PetProfileBuilder().BuildPet(Owner);

        // Act
        var result = pet.AddPhoto("https://cdn.pethost.com/" + new string('a', PetPhoto.UrlMaxLength), Hash("l"), Later);

        // Assert
        result.FirstError!.Message.Should().Contain("500");
    }

    [Fact]
    public void ReplacePhoto_Should_KeepTheIdAndSlotAndReturnThePreviousUrl_When_PhotoExists()
    {
        // Arrange
        var pet = new PetProfileBuilder().BuildPet(Owner, photos: 2);
        var photo = pet.Photos[1];

        // Act
        var result = pet.ReplacePhoto(photo.Id, "https://img.pethost.test/pets/nova.png", Hash("n"), Later);

        // Assert
        result.Value.Should().Be(PetProfileBuilder.PhotoUrl(2));
        pet.Photos[1].Id.Should().Be(photo.Id);
        pet.Photos[1].Position.Should().Be(2);
        pet.Photos[1].Url.Should().Be("https://img.pethost.test/pets/nova.png");
        pet.UpdatedAt.Should().Be(Later);
    }

    [Fact]
    public void ReplacePhoto_Should_Fail_When_PhotoIsNotFromThePet()
    {
        // Arrange
        var pet = new PetProfileBuilder().BuildPet(Owner, photos: 1);

        // Act
        var result = pet.ReplacePhoto(PetPhotoId.New(), "https://img.pethost.test/pets/nova.png", Hash("n"), Later);

        // Assert
        result.FirstError!.Code.Should().Be("PET_PHOTO_NOT_FOUND");
        pet.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void AddPhoto_Should_RejectTheSameImage_When_PetAlreadyHasIt()
    {
        // Arrange
        var pet = new PetProfileBuilder().BuildPet(Owner, photos: 1);

        // Act
        var result = pet.AddPhoto("https://img.pethost.test/pets/outra-url.png", PetProfileBuilder.PhotoHash(1), Later);

        // Assert
        result.FirstError!.Code.Should().Be("PET_PHOTO_ALREADY_EXISTS");
        pet.Photos.Should().HaveCount(1);
        pet.UpdatedAt.Should().Be(Now);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ReplacePhoto_Should_RejectAnImageThePetAlreadyHas_When_ItIsInThisOrAnotherPhoto(int existing)
    {
        // Arrange
        var pet = new PetProfileBuilder().BuildPet(Owner, photos: 2);

        // Act
        var result = pet.ReplacePhoto(pet.Photos[0].Id, "https://img.pethost.test/pets/x.png", PetProfileBuilder.PhotoHash(existing), Later);

        // Assert
        result.FirstError!.Code.Should().Be("PET_PHOTO_ALREADY_EXISTS");
        pet.Photos[0].Url.Should().Be(PetProfileBuilder.PhotoUrl(1));
    }

    [Fact]
    public void RemovePhoto_Should_ReturnTheRemovedPhotoAndKeepTheOthersInPlace_When_PhotoExists()
    {
        // Arrange
        var pet = new PetProfileBuilder().BuildPet(Owner, photos: 3);
        var middle = pet.Photos[1];

        // Act
        var result = pet.RemovePhoto(middle.Id, Later);

        // Assert
        result.Value!.Url.Should().Be(PetProfileBuilder.PhotoUrl(2));
        pet.Photos.Select(p => p.Position).Should().Equal(1, 3);
        pet.CanAddPhoto.Should().BeTrue();
    }

    [Fact]
    public void RemovePhoto_Should_Fail_When_PhotoIsNotFromThePet()
    {
        // Arrange
        var pet = new PetProfileBuilder().BuildPet(Owner, photos: 1);

        // Act
        var result = pet.RemovePhoto(PetPhotoId.New(), Later);

        // Assert
        result.FirstError!.Code.Should().Be("PET_PHOTO_NOT_FOUND");
        pet.Photos.Should().HaveCount(1);
    }

    private static string Hash(string seed) => seed.PadLeft(64, '0');
}
