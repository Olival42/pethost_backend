using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Application.Pets.UpdatePet;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Authorization;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Pets.Application.UnitTests.Pets;

public sealed class UpdatePetCommandHandlerTests
{
    private static readonly DateTimeOffset Later = PetProfileBuilder.DefaultNow.AddDays(1);

    private readonly Mock<IPetRepository> _petRepository = new();
    private readonly Mock<IPetsUnitOfWork> _unitOfWork = new();
    private readonly Mock<IPetKeepers> _petKeepers = PetsTestData.Keepers();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Pet _pet = new PetProfileBuilder().BuildPet(PetsTestData.Owner);
    private readonly UpdatePetCommandHandler _sut;

    public UpdatePetCommandHandlerTests()
    {
        _petRepository.Setup(r => r.GetByIdAsync(_pet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_pet);

        _sut = new UpdatePetCommandHandler(
            _petRepository.Object, _unitOfWork.Object, _petKeepers.Object, _auditTrail.Object, new FakeTimeProvider(Later));
    }

    [Fact]
    public async Task HandleAsync_Should_ChangeOnlyTheSentFields_When_PatchIsPartial()
    {
        // Act
        var result = await _sut.HandleAsync(Patch() with { Name = "Paçoca", Breed = "" }, CancellationToken.None);

        // Assert
        result.Value!.Name.Should().Be("Paçoca");
        result.Value.Breed.Should().BeNull("texto vazio limpa o opcional");
        result.Value.Size.Should().Be("medium", "porte não veio, não muda");
        result.Value.Notes.Should().Be("Morre de medo de fogos.");
        result.Value.UpdatedAt.Should().Be(Later);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_RecordChangedFieldNamesOnly_When_Saved()
    {
        // Act
        await _sut.HandleAsync(Patch() with { Name = "Paçoca", GoodWithCats = true }, CancellationToken.None);

        // Assert
        _auditTrail.Verify(
            a => a.RecordAsync(
                It.Is<AuditRecord>(r => r.Action == AuditActions.PetUpdated && r.Details!["fields"] == "name,goodWithCats"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_NotSave_When_NothingChanged()
    {
        // Act
        var result = await _sut.HandleAsync(Patch() with { Name = "Pipoca" }, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnMicrochipAlreadyRegistered_When_TheNewNumberIsOfAnotherActivePet()
    {
        // Arrange
        _petRepository
            .Setup(r => r.MicrochipInUseAsync("985112004000001", _pet.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.HandleAsync(Patch() with { Microchip = "985112004000001" }, CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("PET_MICROCHIP_ALREADY_REGISTERED");
        _pet.Profile.Microchip.Should().Be("985112004567890");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_NotLookUpTheMicrochip_When_ItDidNotChange()
    {
        // Act
        var result = await _sut.HandleAsync(Patch() with { Name = "Paçoca", Microchip = "985 112 004 567 890" }, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _petRepository.Verify(
            r => r.MicrochipInUseAsync(It.IsAny<string>(), It.IsAny<PetId?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_UpdateTheProfile_When_PetIsInactive()
    {
        // Arrange — o dono corrige a ficha sem precisar reativar.
        _pet.Deactivate(PetProfileBuilder.DefaultNow);

        // Act
        var result = await _sut.HandleAsync(Patch() with { Name = "Paçoca" }, CancellationToken.None);

        // Assert
        result.Value!.Name.Should().Be("Paçoca");
        result.Value.IsActive.Should().BeFalse();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_KeepTheSize_When_DogBecomesACat()
    {
        // Act
        var result = await _sut.HandleAsync(Patch() with { Species = "cat" }, CancellationToken.None);

        // Assert
        result.Value!.Species.Should().Be("cat");
        result.Value.Size.Should().Be("medium", "gato também tem porte");
    }

    [Fact]
    public async Task HandleAsync_Should_ClearTheSize_When_DogBecomesARabbit()
    {
        // Act
        var result = await _sut.HandleAsync(Patch() with { Species = "rabbit" }, CancellationToken.None);

        // Assert
        result.Value!.Species.Should().Be("rabbit");
        result.Value.Size.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnEveryValidationError_When_MergedProfileIsInvalid()
    {
        // Act
        var result = await _sut.HandleAsync(Patch() with { Species = "exotic", Name = " ", PhotoUrl = "ftp://x" }, CancellationToken.None);

        // Assert
        result.Errors!.Select(e => e.Field).Should().BeEquivalentTo(["speciesDescription", "name", "photoUrl"]);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_PetIsFromAnotherAccount()
    {
        // Act
        var result = await _sut.HandleAsync(
            Patch() with { UserId = PetsTestData.StrangerUserId, Name = "Roubada" },
            CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("PET_NOT_FOUND", "pet de outra conta responde como se não existisse");
        _pet.Profile.Name.Should().Be("Pipoca");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_SameIdButOtherRole()
    {
        // Act — a conta de anfitrião da mesma pessoa não é dona do pet do tutor.
        var result = await _sut.HandleAsync(Patch() with { UserId = PetsTestData.HostUserId, Role = Roles.Host }, CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("PET_NOT_FOUND", "pet de outra conta responde como se não existisse");
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_PetDoesNotExist()
    {
        // Act
        var result = await _sut.HandleAsync(Patch() with { PetId = Guid.CreateVersion7() }, CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("PET_NOT_FOUND");
    }

    private UpdatePetCommand Patch() =>
        new(PetsTestData.OwnerUserId, Roles.Owner, _pet.Id.Value,
            null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
}

public sealed class PetPatchTests
{
    private static readonly PetProfileData Dog = new PetProfileBuilder().BuildData();
    private static readonly PetProfileData Exotic = new PetProfileBuilder().AsSpecies("exotic", "Iguana verde").BuildData();

    [Fact]
    public void Apply_Should_KeepEverything_When_NothingIsSent()
    {
        // Act
        var merged = PetPatch.Apply(Dog, Empty());

        // Assert
        merged.Should().Be(Dog);
    }

    [Fact]
    public void Apply_Should_PassEmptyTextAlong_When_ClientClearsAField()
    {
        // Act
        var merged = PetPatch.Apply(Dog, Empty() with { Notes = "", PhotoUrl = "" });

        // Assert
        merged.Notes.Should().Be("");
        merged.PhotoUrl.Should().Be("");
        merged.Name.Should().Be(Dog.Name);
    }

    [Fact]
    public void Apply_Should_DropSizeAndKeepNoDescription_When_SpeciesLeavesDog()
    {
        // Act
        var merged = PetPatch.Apply(Dog, Empty() with { Species = "rabbit" });

        // Assert
        merged.Size.Should().BeNull();
        merged.SpeciesDescription.Should().BeNull();
    }

    [Fact]
    public void Apply_Should_KeepSize_When_DogBecomesACat()
    {
        // Act
        var merged = PetPatch.Apply(Dog, Empty() with { Species = "cat" });

        // Assert
        merged.Size.Should().Be(Dog.Size);
    }

    [Fact]
    public void Apply_Should_DropSize_When_NewSpeciesIsUnknown()
    {
        // Act
        var merged = PetPatch.Apply(Dog, Empty() with { Species = "dragon" });

        // Assert
        merged.Size.Should().BeNull();
    }

    [Fact]
    public void Apply_Should_DropDescription_When_SpeciesLeavesExotic()
    {
        // Act
        var merged = PetPatch.Apply(Exotic, Empty() with { Species = "cat" });

        // Assert
        merged.SpeciesDescription.Should().BeNull();
    }

    [Fact]
    public void Apply_Should_KeepSizeSentTogether_When_SpeciesBecomesDog()
    {
        // Act
        var merged = PetPatch.Apply(Exotic, Empty() with { Species = "dog", Size = "large" });

        // Assert
        merged.Size.Should().Be("large");
        merged.SpeciesDescription.Should().BeNull();
    }

    [Fact]
    public void Apply_Should_KeepSize_When_SpeciesIsResentWithAnotherCase()
    {
        // Act
        var merged = PetPatch.Apply(Dog, Empty() with { Species = " DOG " });

        // Assert
        merged.Size.Should().Be(Dog.Size);
    }

    [Fact]
    public void Apply_Should_ChangeTheWeight_When_Sent()
    {
        // Act
        var merged = PetPatch.Apply(Dog, Empty() with { WeightKg = 16m, Microchip = "" });

        // Assert
        merged.WeightKg.Should().Be(16m);
        merged.Microchip.Should().Be("");
        merged.Allergies.Should().Be(Dog.Allergies);
    }

    [Fact]
    public void ChangedFields_Should_ListNothing_When_ProfilesAreEqual()
    {
        // Act / Assert
        PetPatch.ChangedFields(Dog, Dog).Should().BeEmpty();
    }

    [Fact]
    public void ChangedFields_Should_ListEveryChangedField_When_ManyChanged()
    {
        // Act
        var changed = PetPatch.ChangedFields(Dog, Exotic with { Name = "Iggy" });

        // Assert
        changed.Should().Equal("species", "speciesDescription", "name", "size");
    }

    private static UpdatePetCommand Empty() =>
        new(Guid.CreateVersion7(), Roles.Owner, Guid.CreateVersion7(),
            null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
}
