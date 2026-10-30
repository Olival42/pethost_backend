using FluentAssertions;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Pets.Domain.UnitTests.Pets;

public sealed class PetTests
{
    private static readonly DateTimeOffset Now = PetProfileBuilder.DefaultNow;
    private static readonly Guid OwnerId = Guid.CreateVersion7();
    private static readonly Guid HostId = Guid.CreateVersion7();

    [Fact]
    public void Create_Should_PointToTheOwnerOnly_When_KeeperIsAnOwner()
    {
        // Act
        var pet = Pet.Create(PetKeeper.Owner(OwnerId), new PetProfileBuilder().Build(), Now);

        // Assert
        pet.Id.Value.Should().NotBe(Guid.Empty);
        pet.OwnerId.Should().Be(OwnerId);
        pet.HostId.Should().BeNull();
        pet.Keeper.Should().Be(PetKeeper.Owner(OwnerId));
        pet.IsActive.Should().BeTrue();
        pet.DeactivatedAt.Should().BeNull();
        pet.CreatedAt.Should().Be(Now);
        pet.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_Should_PointToTheHostOnly_When_KeeperIsAHost()
    {
        // Act
        var pet = Pet.Create(PetKeeper.Host(HostId), new PetProfileBuilder().Build(), Now);

        // Assert
        pet.HostId.Should().Be(HostId);
        pet.OwnerId.Should().BeNull();
        pet.Keeper.Should().Be(PetKeeper.Host(HostId));
    }

    [Fact]
    public void Create_Should_Throw_When_KeeperHasNoId()
    {
        // Act
        var act = () => Pet.Create(default, new PetProfileBuilder().Build(), Now);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IsKeptBy_Should_TellOwnerAndHostApart_When_IdsAreTheSame()
    {
        // Arrange
        var sameId = Guid.CreateVersion7();
        var pet = Pet.Create(PetKeeper.Owner(sameId), new PetProfileBuilder().Build(), Now);

        // Act / Assert
        pet.IsKeptBy(PetKeeper.Owner(sameId)).Should().BeTrue();
        pet.IsKeptBy(PetKeeper.Host(sameId)).Should().BeFalse("tutor e anfitrião são donos diferentes");
        pet.IsKeptBy(PetKeeper.Owner(Guid.CreateVersion7())).Should().BeFalse();
    }

    [Fact]
    public void UpdateProfile_Should_ReplaceProfileAndTouchUpdatedAt_When_SomethingChanged()
    {
        // Arrange
        var pet = Pet.Create(PetKeeper.Owner(OwnerId), new PetProfileBuilder().Build(), Now);
        var renamed = new PetProfileBuilder().WithName("Paçoca").Build();

        // Act
        var changed = pet.UpdateProfile(renamed, Now.AddDays(1));

        // Assert
        changed.Should().BeTrue();
        pet.Profile.Name.Should().Be("Paçoca");
        pet.UpdatedAt.Should().Be(Now.AddDays(1));
        pet.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void UpdateProfile_Should_NotTouchUpdatedAt_When_ProfileIsTheSame()
    {
        // Arrange
        var pet = Pet.Create(PetKeeper.Owner(OwnerId), new PetProfileBuilder().Build(), Now);

        // Act
        var changed = pet.UpdateProfile(new PetProfileBuilder().Build(), Now.AddDays(1));

        // Assert
        changed.Should().BeFalse();
        pet.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Deactivate_Should_MarkInactiveWithDate_When_PetIsActive()
    {
        // Arrange
        var pet = Pet.Create(PetKeeper.Owner(OwnerId), new PetProfileBuilder().Build(), Now);

        // Act
        pet.Deactivate(Now.AddDays(2));

        // Assert
        pet.IsActive.Should().BeFalse();
        pet.DeactivatedAt.Should().Be(Now.AddDays(2));
        pet.UpdatedAt.Should().Be(Now.AddDays(2));
    }

    [Fact]
    public void Deactivate_Should_KeepTheFirstDate_When_CalledTwice()
    {
        // Arrange
        var pet = Pet.Create(PetKeeper.Owner(OwnerId), new PetProfileBuilder().Build(), Now);
        pet.Deactivate(Now.AddDays(2));

        // Act
        pet.Deactivate(Now.AddDays(5));

        // Assert
        pet.DeactivatedAt.Should().Be(Now.AddDays(2));
        pet.UpdatedAt.Should().Be(Now.AddDays(2));
    }

    [Fact]
    public void Reactivate_Should_ClearDeactivation_When_PetIsInactive()
    {
        // Arrange
        var pet = Pet.Create(PetKeeper.Owner(OwnerId), new PetProfileBuilder().Build(), Now);
        pet.Deactivate(Now.AddDays(2));

        // Act
        pet.Reactivate(Now.AddDays(3));

        // Assert
        pet.IsActive.Should().BeTrue();
        pet.DeactivatedAt.Should().BeNull();
        pet.UpdatedAt.Should().Be(Now.AddDays(3));
    }

    [Fact]
    public void Reactivate_Should_ChangeNothing_When_PetIsActive()
    {
        // Arrange
        var pet = Pet.Create(PetKeeper.Owner(OwnerId), new PetProfileBuilder().Build(), Now);

        // Act
        pet.Reactivate(Now.AddDays(3));

        // Assert
        pet.UpdatedAt.Should().Be(Now);
    }
}
