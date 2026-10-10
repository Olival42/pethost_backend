using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Application.Pets.RegisterPet;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Pets.Application.UnitTests.Pets;

public sealed class RegisterPetCommandHandlerTests
{
    private readonly Mock<IPetRepository> _petRepository = new();
    private readonly Mock<IPetsUnitOfWork> _unitOfWork = new();
    private readonly Mock<IPetKeepers> _petKeepers = PetsTestData.Keepers();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly RegisterPetCommandHandler _sut;

    public RegisterPetCommandHandlerTests()
    {
        _sut = new RegisterPetCommandHandler(
            _petRepository.Object, _unitOfWork.Object, _petKeepers.Object, _auditTrail.Object, new FakeTimeProvider(PetProfileBuilder.DefaultNow));
    }

    [Fact]
    public async Task HandleAsync_Should_SavePetOfTheOwner_When_AccountIsAnOwner()
    {
        // Arrange
        Pet? saved = null;
        _petRepository.Setup(r => r.Add(It.IsAny<Pet>())).Callback<Pet>(p => saved = p);

        // Act
        var result = await _sut.HandleAsync(Command(PetsTestData.OwnerUserId, Roles.Owner), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.OwnerId.Should().Be(PetsTestData.OwnerId);
        result.Value.HostId.Should().BeNull();
        result.Value.Keeper!.Name.Should().Be("Camila Souza");
        result.Value.Species.Should().Be("dog");
        result.Value.Size.Should().Be("medium");
        saved!.Keeper.Should().Be(PetsTestData.Owner);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_SavePetOfTheHost_When_AccountIsAHost()
    {
        // Act
        var result = await _sut.HandleAsync(Command(PetsTestData.HostUserId, Roles.Host), CancellationToken.None);

        // Assert
        result.Value!.HostId.Should().Be(PetsTestData.HostId);
        result.Value.OwnerId.Should().BeNull();
        result.Value.Keeper!.Type.Should().Be("host");
    }

    [Fact]
    public async Task HandleAsync_Should_RecordTheRegistrationInTheTrail_When_Saved()
    {
        // Act
        var result = await _sut.HandleAsync(Command(PetsTestData.OwnerUserId, Roles.Owner), CancellationToken.None);

        // Assert
        _auditTrail.Verify(
            a => a.RecordAsync(
                It.Is<AuditRecord>(r =>
                    r.Action == AuditActions.PetRegistered
                    && r.TargetType == AuditTargets.Pet
                    && r.TargetId == result.Value!.Id
                    && r.Details!["keeper"] == "owner"
                    && r.Details["species"] == "dog"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnKeeperNotFound_When_AccountHasNoProfile()
    {
        // Act
        var result = await _sut.HandleAsync(Command(Guid.CreateVersion7(), Roles.Host), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("PET_KEEPER_NOT_FOUND");
        _petRepository.Verify(r => r.Add(It.IsAny<Pet>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnValidationErrors_When_ProfileIsInvalid()
    {
        // Arrange — o validador já barraria; o domínio é a segunda garantia.
        var command = Command(PetsTestData.OwnerUserId, Roles.Owner) with { Size = null };

        // Act
        var result = await _sut.HandleAsync(command, CancellationToken.None);

        // Assert
        result.FirstError!.Field.Should().Be("size");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnMicrochipAlreadyRegistered_When_AnotherActivePetHasIt()
    {
        // Arrange
        _petRepository
            .Setup(r => r.MicrochipInUseAsync("985112004567890", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.HandleAsync(
            Command(PetsTestData.OwnerUserId, Roles.Owner) with { Microchip = "985 112 004 567 890" },
            CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("PET_MICROCHIP_ALREADY_REGISTERED");
        _petRepository.Verify(r => r.Add(It.IsAny<Pet>()), Times.Never);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_NotLookUpTheMicrochip_When_NoneIsSent()
    {
        // Act
        var result = await _sut.HandleAsync(Command(PetsTestData.OwnerUserId, Roles.Owner) with { Microchip = null }, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _petRepository.Verify(
            r => r.MicrochipInUseAsync(It.IsAny<string>(), It.IsAny<PetId?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    internal static RegisterPetCommand Command(Guid userId, string role)
    {
        var data = new PetProfileBuilder().BuildData();

        return new RegisterPetCommand(
            userId,
            role,
            data.Species,
            data.SpeciesDescription,
            data.Name,
            data.PhotoUrl,
            data.Breed,
            data.Size,
            data.BirthDate,
            data.Sex,
            data.IsNeutered,
            data.IsVaccinated,
            data.MedicationNotes,
            data.FeedingNotes,
            data.GoodWithDogs,
            data.GoodWithCats,
            data.GoodWithKids,
            data.VetContact,
            data.Notes);
    }
}

public sealed class RegisterPetCommandValidatorTests
{
    private readonly RegisterPetCommandValidator _sut = new(new FakeTimeProvider(PetProfileBuilder.DefaultNow));

    [Fact]
    public async Task ValidateAsync_Should_Pass_When_ProfileIsValid()
    {
        // Act
        var result = await _sut.ValidateAsync(RegisterPetCommandHandlerTests.Command(Guid.CreateVersion7(), Roles.Owner), TestContext.Current.CancellationToken);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_Should_ReportEveryFieldOfTheDomain_When_ManyAreWrong()
    {
        // Arrange
        var command = RegisterPetCommandHandlerTests.Command(Guid.CreateVersion7(), Roles.Owner) with
        {
            Species = "rabbit",
            Size = "large",
            Name = "",
            PhotoUrl = "javascript:alert(1)",
            Sex = null,
            GoodWithKids = null,
        };

        // Act
        var result = await _sut.ValidateAsync(command, TestContext.Current.CancellationToken);

        // Assert
        result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["size", "name", "photoUrl", "sex", "goodWithKids"]);
    }
}
