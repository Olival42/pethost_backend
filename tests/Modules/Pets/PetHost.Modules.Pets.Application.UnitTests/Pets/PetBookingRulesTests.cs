using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Modules.Pets.Application.Pets.DeactivatePet;
using PetHost.Modules.Pets.Application.Pets.GetPetById;
using PetHost.Modules.Pets.Application.Pets.ListHostPets;
using PetHost.Modules.Pets.Domain.Errors;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Shared.Contracts.Authorization;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Pets.Application.UnitTests.Pets;

/// <summary>O que as reservas e a situação do anfitrião mudam no Pets.</summary>
public sealed class PetBookingRulesTests
{
    private readonly Mock<IPetRepository> _petRepository = new();
    private readonly Mock<IPetsUnitOfWork> _unitOfWork = new();
    private readonly Mock<IPetKeepers> _petKeepers = PetsTestData.Keepers();
    private readonly Mock<IPetBookings> _petBookings = PetsTestData.Bookings();
    private readonly Pet _ownerPet = new PetProfileBuilder().BuildPet(PetsTestData.Owner);
    private readonly Pet _hostPet = new PetProfileBuilder().WithName("Thor").BuildPet(PetsTestData.Host);

    public PetBookingRulesTests()
    {
        _petRepository.Setup(r => r.GetByIdAsync(_ownerPet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_ownerPet);
        _petRepository.Setup(r => r.GetByIdAsync(_hostPet.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_hostPet);
    }

    private GetPetByIdQueryHandler GetById() => new(_petRepository.Object, _petKeepers.Object, _petBookings.Object);

    private DeactivatePetCommandHandler Deactivate() => new(
        _petRepository.Object,
        _unitOfWork.Object,
        _petKeepers.Object,
        _petBookings.Object,
        new Mock<Shared.Contracts.Audit.IAuditTrail>().Object,
        new FakeTimeProvider(PetProfileBuilder.DefaultNow.AddDays(1)));

    // ----- desativar com reserva em aberto -----

    [Fact]
    public async Task Deactivate_Should_ReturnHasOpenBooking_When_OwnersPetHasAnOpenStay()
    {
        // Arrange
        _petBookings.Setup(b => b.HasOpenBookingAsync(_ownerPet.Id.Value, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var result = await Deactivate().HandleAsync(new DeactivatePetCommand(PetsTestData.OwnerUserId, Roles.Owner, _ownerPet.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(PetsErrors.HasOpenBooking);
        _ownerPet.IsActive.Should().BeTrue();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Deactivate_Should_Succeed_When_OwnersPetHasNoOpenStay()
    {
        // Act
        var result = await Deactivate().HandleAsync(new DeactivatePetCommand(PetsTestData.OwnerUserId, Roles.Owner, _ownerPet.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Deactivate_Should_NotAskTheBookings_When_PetBelongsToAHost()
    {
        // Act
        await Deactivate().HandleAsync(new DeactivatePetCommand(PetsTestData.HostUserId, Roles.Host, _hostPet.Id.Value), CancellationToken.None);

        // Assert
        _petBookings.Verify(b => b.HasOpenBookingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ----- ficha do pet para o anfitrião da reserva -----

    [Fact]
    public async Task GetById_Should_ShowTheOwnersPetToTheHost_When_ThePetWasBookedWithThem()
    {
        // Arrange
        _petBookings.Setup(b => b.IsBookedWithHostAsync(_ownerPet.Id.Value, PetsTestData.HostId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var result = await GetById().HandleAsync(new GetPetByIdQuery(PetsTestData.HostUserId, Roles.Host, _ownerPet.Id.Value), CancellationToken.None);

        // Assert
        result.Value!.Name.Should().Be("Pipoca");
    }

    [Fact]
    public async Task GetById_Should_HideTheOwnersPetFromAHost_When_ThereIsNoBooking()
    {
        // Act
        var result = await GetById().HandleAsync(new GetPetByIdQuery(PetsTestData.HostUserId, Roles.Host, _ownerPet.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(PetsErrors.NotFound(_ownerPet.Id));
    }

    // ----- anfitrião inativo ou suspenso -----

    [Fact]
    public async Task GetById_Should_HideTheHostsPetFromOthers_When_HostIsUnavailable()
    {
        // Arrange
        _petKeepers.Setup(k => k.IsHostAvailableAsync(PetsTestData.HostId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        // Act
        var result = await GetById().HandleAsync(new GetPetByIdQuery(PetsTestData.OwnerUserId, Roles.Owner, _hostPet.Id.Value), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(PetsErrors.NotFound(_hostPet.Id));
    }

    [Fact]
    public async Task GetById_Should_StillShowTheHostTheirOwnPet_When_HostIsUnavailable()
    {
        // Arrange
        _petKeepers.Setup(k => k.IsHostAvailableAsync(PetsTestData.HostId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        // Act
        var result = await GetById().HandleAsync(new GetPetByIdQuery(PetsTestData.HostUserId, Roles.Host, _hostPet.Id.Value), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ListHostPets_Should_ReturnHostNotFound_When_HostIsUnavailable()
    {
        // Arrange
        _petKeepers.Setup(k => k.IsHostAvailableAsync(PetsTestData.HostId, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var sut = new ListHostPetsQueryHandler(_petRepository.Object, _petKeepers.Object);

        // Act
        var result = await sut.HandleAsync(new ListHostPetsQuery(PetsTestData.HostId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(PetsErrors.HostNotFound(PetsTestData.HostId));
    }
}
