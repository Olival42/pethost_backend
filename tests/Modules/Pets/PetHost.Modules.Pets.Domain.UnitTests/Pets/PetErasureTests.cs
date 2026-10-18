using FluentAssertions;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Pets.Domain.UnitTests.Pets;

/// <summary>A conta do dono foi excluída (LGPD): o pet sai do ar e perde as fotos.</summary>
public sealed class PetErasureTests
{
    private static readonly DateTimeOffset Now = PetProfileBuilder.DefaultNow;
    private static readonly DateTimeOffset Later = Now.AddDays(5);

    private static Pet AnyPet() => Pet.Create(PetKeeper.Owner(Guid.CreateVersion7()), new PetProfileBuilder().Build(), Now);

    private static string Hash(string seed) => string.Concat(Enumerable.Repeat(seed, 64))[..64];

    [Fact]
    public void Erase_Should_RemoveEveryPhotoAndReturnTheUrls_When_PetHasPhotos()
    {
        // Arrange
        var pet = AnyPet();
        pet.AddPhoto("https://img.pethost.test/pets/a.png", Hash("a"), Now);
        pet.AddPhoto("https://img.pethost.test/pets/b.png", Hash("b"), Now);

        // Act
        var urls = pet.Erase(Later);

        // Assert
        urls.Should().BeEquivalentTo("https://img.pethost.test/pets/a.png", "https://img.pethost.test/pets/b.png");
        pet.Photos.Should().BeEmpty();
        pet.IsActive.Should().BeFalse();
        pet.DeactivatedAt.Should().Be(Later);
        pet.UpdatedAt.Should().Be(Later);
    }

    [Fact]
    public void Erase_Should_KeepTheProfile_When_Called()
    {
        // Arrange
        var pet = AnyPet();
        var profile = pet.Profile;

        // Act
        pet.Erase(Later);

        // Assert
        pet.Profile.Should().Be(profile, "reservas e avaliações antigas mostram o pet");
    }

    [Fact]
    public void Erase_Should_ChangeNothing_When_AlreadyErased()
    {
        // Arrange
        var pet = AnyPet();
        pet.Erase(Later);

        // Act
        var urls = pet.Erase(Later.AddDays(1));

        // Assert
        urls.Should().BeEmpty();
        pet.UpdatedAt.Should().Be(Later);
    }
}
