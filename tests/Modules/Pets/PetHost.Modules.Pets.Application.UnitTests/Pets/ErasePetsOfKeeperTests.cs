using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Pets.Application.Pets.ErasePetsOfKeeper;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Storage;
using PetHost.TestKit;
using Xunit;
using static PetHost.Modules.Pets.Application.UnitTests.Pets.PetsTestData;

namespace PetHost.Modules.Pets.Application.UnitTests.Pets;

/// <summary>A conta do dono foi excluída (LGPD): os pets saem do ar e perdem as fotos.</summary>
public sealed class ErasePetsOfKeeperTests
{
    private const string PhotoUrl = "https://img.pethost.test/pets/thor.png";

    private static readonly DateTimeOffset Later = PetProfileBuilder.DefaultNow.AddDays(10);

    private readonly Mock<IPetRepository> _pets = new();
    private readonly Mock<IPetsUnitOfWork> _unitOfWork = new();
    private readonly Mock<IImageStorage> _images = new();
    private readonly Pet _withPhoto = Pet.Create(Owner, new PetProfileBuilder().Build(), PetProfileBuilder.DefaultNow);
    private readonly Pet _withoutPhoto = Pet.Create(Owner, new PetProfileBuilder().Build(), PetProfileBuilder.DefaultNow);
    private readonly List<string> _calls = [];

    public ErasePetsOfKeeperTests()
    {
        _withPhoto.AddPhoto(PhotoUrl, new string('a', 64), PetProfileBuilder.DefaultNow);

        _pets.Setup(p => p.ListByKeeperForUpdateAsync(Owner, It.IsAny<CancellationToken>())).ReturnsAsync([_withPhoto, _withoutPhoto]);
        _unitOfWork.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).Callback(() => _calls.Add("save")).ReturnsAsync(1);
        _images
            .Setup(s => s.DeleteAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("photo"))
            .Returns(Task.CompletedTask);
    }

    private ErasePetsOfKeeperCommandHandler Handler() => new(_pets.Object, _unitOfWork.Object, _images.Object, new FakeTimeProvider(Later));

    [Fact]
    public async Task HandleAsync_Should_EraseEveryPetAndThenThePhotos_When_KeeperHasPets()
    {
        // Act
        var result = await Handler().HandleAsync(new ErasePetsOfKeeperCommand(KeeperType.Owner, OwnerId), CancellationToken.None);

        // Assert
        result.Value.Should().Be(2);
        _withPhoto.IsActive.Should().BeFalse();
        _withPhoto.Photos.Should().BeEmpty();
        _withoutPhoto.IsActive.Should().BeFalse();
        _calls.Should().Equal(["save", "photo"], "a foto sai do bucket só depois que o banco gravou");
        _images.Verify(s => s.DeleteAsync(PhotoUrl, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_LookForTheHostsPets_When_KeeperIsAHost()
    {
        // Arrange
        _pets.Setup(p => p.ListByKeeperForUpdateAsync(Host, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        // Act
        var result = await Handler().HandleAsync(new ErasePetsOfKeeperCommand(KeeperType.Host, HostId), CancellationToken.None);

        // Assert
        result.Value.Should().Be(0);
        _pets.Verify(p => p.ListByKeeperForUpdateAsync(Host, It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Validator_Should_RequireTheKeeperId_When_Empty()
    {
        // Act
        var result = new ErasePetsOfKeeperCommandValidator().Validate(new ErasePetsOfKeeperCommand(KeeperType.Owner, Guid.Empty));

        // Assert
        result.Errors.Should().ContainSingle(e => e.PropertyName == "KeeperId");
    }
}
