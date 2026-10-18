using FluentAssertions;
using PetHost.Modules.Owners.Domain.Owners;
using Xunit;

namespace PetHost.Modules.Owners.Domain.UnitTests.Owners;

/// <summary>Exclusão da conta a pedido do titular (LGPD): o perfil perde o CPF e não volta.</summary>
public sealed class OwnerErasureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    private static Owner AnyOwner => Owner.Create(Guid.CreateVersion7(), Cpf.Create("52998224725").Value!, Now.AddDays(-30));

    [Fact]
    public void Erase_Should_DropTheCpfAndDeactivate_When_Called()
    {
        var owner = AnyOwner;

        owner.Erase(Now);

        owner.Cpf.Should().BeNull();
        owner.IsActive.Should().BeFalse();
        owner.DeactivatedAt.Should().Be(Now);
        owner.IsErased.Should().BeTrue();
        owner.ErasedAt.Should().Be(Now);
        owner.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Erase_Should_KeepTheFirstErasure_When_CalledTwice()
    {
        var owner = AnyOwner;
        owner.Erase(Now);

        owner.Erase(Now.AddDays(1));

        owner.ErasedAt.Should().Be(Now);
    }

    [Fact]
    public void Reactivate_Should_DoNothing_When_Erased()
    {
        var owner = AnyOwner;
        owner.Erase(Now);

        owner.Reactivate(Now.AddDays(1));

        owner.IsActive.Should().BeFalse();
    }
}
