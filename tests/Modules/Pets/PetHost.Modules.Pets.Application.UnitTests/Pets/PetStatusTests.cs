using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Application.Pets.DeactivatePet;
using PetHost.Modules.Pets.Application.Pets.ReactivatePet;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Pets.Application.UnitTests.Pets;

public sealed class DeactivatePetCommandHandlerTests
{
    private static readonly DateTimeOffset Later = PetProfileBuilder.DefaultNow.AddDays(3);

    private readonly Mock<IPetRepository> _petRepository = new();
    private readonly Mock<IPetsUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Pet _pet = new PetProfileBuilder().BuildPet(PetsTestData.Host);
    private readonly DeactivatePetCommandHandler _sut;

    public DeactivatePetCommandHandlerTests()
    {
        _petRepository.Setup(r => r.GetByIdAsync(_pet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_pet);

        _sut = new DeactivatePetCommandHandler(
            _petRepository.Object, _unitOfWork.Object, PetsTestData.Keepers().Object, _auditTrail.Object, new FakeTimeProvider(Later));
    }

    [Fact]
    public async Task HandleAsync_Should_DeactivateAndRecord_When_PetIsActive()
    {
        // Act
        var result = await _sut.HandleAsync(new DeactivatePetCommand(PetsTestData.HostUserId, Roles.Host, _pet.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.IsActive.Should().BeFalse();
        result.Value.DeactivatedAt.Should().Be(Later);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(
            a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.PetDeactivated), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_NotSaveAgain_When_PetIsAlreadyInactive()
    {
        // Arrange
        _pet.Deactivate(PetProfileBuilder.DefaultNow);

        // Act
        var result = await _sut.HandleAsync(new DeactivatePetCommand(PetsTestData.HostUserId, Roles.Host, _pet.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.DeactivatedAt.Should().Be(PetProfileBuilder.DefaultNow);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_PetIsFromAnotherAccount()
    {
        // Act
        var result = await _sut.HandleAsync(new DeactivatePetCommand(PetsTestData.OwnerUserId, Roles.Owner, _pet.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("PET_NOT_FOUND", "pet de outra conta responde como se não existisse");
        _pet.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_PetDoesNotExist()
    {
        // Act
        var result = await _sut.HandleAsync(
            new DeactivatePetCommand(PetsTestData.HostUserId, Roles.Host, Guid.CreateVersion7()), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("PET_NOT_FOUND");
    }
}

public sealed class ReactivatePetCommandHandlerTests
{
    private static readonly DateTimeOffset Later = PetProfileBuilder.DefaultNow.AddDays(3);

    private readonly Mock<IPetRepository> _petRepository = new();
    private readonly Mock<IPetsUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Pet _pet = new PetProfileBuilder().BuildPet(PetsTestData.Owner);
    private readonly ReactivatePetCommandHandler _sut;

    public ReactivatePetCommandHandlerTests()
    {
        _pet.Deactivate(PetProfileBuilder.DefaultNow);
        _petRepository.Setup(r => r.GetByIdAsync(_pet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_pet);

        _sut = new ReactivatePetCommandHandler(
            _petRepository.Object, _unitOfWork.Object, PetsTestData.Keepers().Object, _auditTrail.Object, new FakeTimeProvider(Later));
    }

    [Fact]
    public async Task HandleAsync_Should_ReactivateAndRecord_When_PetIsInactive()
    {
        // Act
        var result = await _sut.HandleAsync(new ReactivatePetCommand(PetsTestData.OwnerUserId, Roles.Owner, _pet.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.IsActive.Should().BeTrue();
        result.Value.DeactivatedAt.Should().BeNull();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(
            a => a.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.PetReactivated), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnMicrochipAlreadyRegistered_When_AnotherActivePetTookTheNumber()
    {
        // Arrange — enquanto estava desativado, outro pet ativo ficou com o mesmo microchip.
        _petRepository
            .Setup(r => r.MicrochipInUseAsync("985112004567890", _pet.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.HandleAsync(new ReactivatePetCommand(PetsTestData.OwnerUserId, Roles.Owner, _pet.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("PET_MICROCHIP_ALREADY_REGISTERED");
        _pet.IsActive.Should().BeFalse();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_NotSave_When_PetIsAlreadyActive()
    {
        // Arrange
        _pet.Reactivate(PetProfileBuilder.DefaultNow);

        // Act
        await _sut.HandleAsync(new ReactivatePetCommand(PetsTestData.OwnerUserId, Roles.Owner, _pet.Id.Value), CancellationToken.None);

        // Assert
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_PetIsFromAnotherAccount()
    {
        // Act
        var result = await _sut.HandleAsync(new ReactivatePetCommand(PetsTestData.StrangerUserId, Roles.Owner, _pet.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("PET_NOT_FOUND", "pet de outra conta responde como se não existisse");
        _pet.IsActive.Should().BeFalse();
    }
}
