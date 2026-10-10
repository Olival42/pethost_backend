using FluentAssertions;
using Moq;
using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Application.Pets.GetPetById;
using PetHost.Modules.Pets.Application.Pets.ListHostPets;
using PetHost.Modules.Pets.Application.Pets.ListMyPets;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Authorization;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Pets.Application.UnitTests.Pets;

public sealed class GetPetByIdQueryHandlerTests
{
    private readonly Mock<IPetRepository> _petRepository = new();
    private readonly Mock<IPetKeepers> _petKeepers = PetsTestData.Keepers();
    private readonly Pet _ownerPet = new PetProfileBuilder().BuildPet(PetsTestData.Owner);
    private readonly Pet _hostPet = new PetProfileBuilder().WithName("Thor").BuildPet(PetsTestData.Host);
    private readonly GetPetByIdQueryHandler _sut;

    public GetPetByIdQueryHandlerTests()
    {
        _petRepository.Setup(r => r.GetByIdAsync(_ownerPet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_ownerPet);
        _petRepository.Setup(r => r.GetByIdAsync(_hostPet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_hostPet);

        _sut = new GetPetByIdQueryHandler(_petRepository.Object, _petKeepers.Object);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnThePetWithItsKeeper_When_RequesterIsTheOwner()
    {
        // Arrange
        _ownerPet.Deactivate(PetProfileBuilder.DefaultNow);

        // Act
        var result = await _sut.HandleAsync(new GetPetByIdQuery(PetsTestData.OwnerUserId, Roles.Owner, _ownerPet.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.Name.Should().Be("Pipoca");
        result.Value.IsActive.Should().BeFalse("o dono vê o próprio pet mesmo inativo");
        result.Value.Keeper!.Name.Should().Be("Camila Souza");
    }

    [Fact]
    public async Task HandleAsync_Should_HideAnotherOwnersPet_When_RequesterIsNotTheOwner()
    {
        // Act
        var result = await _sut.HandleAsync(new GetPetByIdQuery(PetsTestData.StrangerUserId, Roles.Owner, _ownerPet.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("PET_NOT_FOUND");
    }

    [Fact]
    public async Task HandleAsync_Should_ShowAHostsActivePet_When_RequesterIsAnyOwner()
    {
        // Act
        var result = await _sut.HandleAsync(new GetPetByIdQuery(PetsTestData.StrangerUserId, Roles.Owner, _hostPet.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.Name.Should().Be("Thor");
        result.Value.Keeper!.Name.Should().Be("Dona Cida");
    }

    [Fact]
    public async Task HandleAsync_Should_HideAHostsInactivePet_When_RequesterIsNotTheHost()
    {
        // Arrange
        _hostPet.Deactivate(PetProfileBuilder.DefaultNow);

        // Act
        var result = await _sut.HandleAsync(new GetPetByIdQuery(PetsTestData.OwnerUserId, Roles.Owner, _hostPet.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("PET_NOT_FOUND");
    }

    [Fact]
    public async Task HandleAsync_Should_ShowAnyPet_When_RequesterIsAdmin()
    {
        // Act
        var result = await _sut.HandleAsync(new GetPetByIdQuery(Guid.CreateVersion7(), Roles.Admin, _ownerPet.Id.Value), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_PetDoesNotExist()
    {
        // Act
        var result = await _sut.HandleAsync(new GetPetByIdQuery(PetsTestData.OwnerUserId, Roles.Owner, Guid.CreateVersion7()), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("PET_NOT_FOUND");
    }
}

public sealed class ListMyPetsQueryHandlerTests
{
    private readonly Mock<IPetRepository> _petRepository = new();
    private readonly ListMyPetsQueryHandler _sut;

    public ListMyPetsQueryHandlerTests()
    {
        _sut = new ListMyPetsQueryHandler(_petRepository.Object, PetsTestData.Keepers().Object);
    }

    [Fact]
    public async Task HandleAsync_Should_ListThePetsOfTheLoggedKeeper_When_ProfileExists()
    {
        // Arrange
        IReadOnlyList<Pet> pets =
        [
            new PetProfileBuilder().BuildPet(PetsTestData.Host),
            new PetProfileBuilder().WithName("Mia").AsSpecies("cat").BuildPet(PetsTestData.Host),
        ];
        _petRepository.Setup(r => r.ListByKeeperAsync(PetsTestData.Host, It.IsAny<CancellationToken>())).ReturnsAsync(pets);

        // Act
        var result = await _sut.HandleAsync(new ListMyPetsQuery(PetsTestData.HostUserId, Roles.Host), CancellationToken.None);

        // Assert
        result.Value!.Keeper!.Name.Should().Be("Dona Cida");
        result.Value.Keeper.Id.Should().Be(PetsTestData.HostId);
        result.Value.Pets.Select(p => p.Name).Should().Equal("Pipoca", "Mia");
        result.Value.Pets.Should().OnlyContain(
            p => p.Keeper == null && p.HostId == null && p.OwnerId == null,
            "o dono vem uma vez, no topo");
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnKeeperNotFound_When_AccountHasNoProfile()
    {
        // Act
        var result = await _sut.HandleAsync(new ListMyPetsQuery(Guid.CreateVersion7(), Roles.Host), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("PET_KEEPER_NOT_FOUND");
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnEmptyList_When_KeeperHasNoPets()
    {
        // Arrange
        _petRepository.Setup(r => r.ListByKeeperAsync(PetsTestData.Owner, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        // Act
        var result = await _sut.HandleAsync(new ListMyPetsQuery(PetsTestData.OwnerUserId, Roles.Owner), CancellationToken.None);

        // Assert
        result.Value!.Pets.Should().BeEmpty();
        result.Value.Keeper!.Name.Should().Be("Camila Souza", "o dono vem mesmo sem pets");
    }
}

public sealed class ListHostPetsQueryHandlerTests
{
    private readonly Mock<IPetRepository> _petRepository = new();
    private readonly ListHostPetsQueryHandler _sut;

    public ListHostPetsQueryHandlerTests()
    {
        _sut = new ListHostPetsQueryHandler(_petRepository.Object, PetsTestData.Keepers().Object);
    }

    [Fact]
    public async Task HandleAsync_Should_ListTheActivePetsOfTheHost_When_HostExists()
    {
        // Arrange
        IReadOnlyList<Pet> pets = [new PetProfileBuilder().WithName("Thor").BuildPet(PetsTestData.Host)];
        _petRepository.Setup(r => r.ListActiveByKeeperAsync(PetsTestData.Host, It.IsAny<CancellationToken>())).ReturnsAsync(pets);

        // Act
        var result = await _sut.HandleAsync(new ListHostPetsQuery(PetsTestData.HostId), CancellationToken.None);

        // Assert
        result.Value!.Keeper!.Name.Should().Be("Dona Cida");
        result.Value.Pets.Should().ContainSingle(p => p.Name == "Thor" && p.Keeper == null);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnHostNotFound_When_HostDoesNotExist()
    {
        // Act
        var result = await _sut.HandleAsync(new ListHostPetsQuery(Guid.CreateVersion7()), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("PET_HOST_NOT_FOUND");
        _petRepository.Verify(r => r.ListActiveByKeeperAsync(It.IsAny<PetKeeper>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnHostNotFound_When_IdIsEmpty()
    {
        // Act
        var result = await _sut.HandleAsync(new ListHostPetsQuery(Guid.Empty), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("PET_HOST_NOT_FOUND");
    }
}
