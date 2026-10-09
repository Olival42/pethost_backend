using FluentAssertions;
using PetHost.Modules.Owners.Domain.Owners;
using Xunit;

namespace PetHost.Modules.Owners.Domain.UnitTests.Owners;

public sealed class OwnerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static Cpf AnyCpf => Cpf.Create("52998224725").Value!;

    [Fact]
    public void Create_Should_GenerateOwnIdAndPointToTheAccount_When_Created()
    {
        var owner = Owner.Create(UserId, AnyCpf, Now);

        owner.Id.Value.Should().NotBe(Guid.Empty);
        owner.Id.Value.Should().NotBe(UserId, "o tutor tem id próprio; a conta é só referência");
        owner.UserId.Should().Be(UserId);
        owner.Cpf.Should().Be(AnyCpf);
        owner.StripeCustomerId.Should().BeNull("o Customer só é criado no primeiro pagamento");
        owner.IsActive.Should().BeTrue();
        owner.DeactivatedAt.Should().BeNull();
        owner.CreatedAt.Should().Be(Now);
        owner.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_Should_GenerateDifferentIds_When_CalledTwice()
    {
        Owner.Create(UserId, AnyCpf, Now).Id.Should().NotBe(Owner.Create(UserId, AnyCpf, Now).Id);
    }

    [Fact]
    public void Create_Should_Throw_When_AccountIsEmpty()
    {
        var act = () => Owner.Create(Guid.Empty, AnyCpf, Now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LinkStripeCustomer_Should_StoreIdAndTouchUpdatedAt_When_Called()
    {
        var owner = Owner.Create(UserId, AnyCpf, Now);

        owner.LinkStripeCustomer("cus_123", Now.AddDays(2));

        owner.StripeCustomerId.Should().Be("cus_123");
        owner.UpdatedAt.Should().Be(Now.AddDays(2));
        owner.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public void ChangeCpf_Should_ReplaceCpf_When_NoPaymentWasMade()
    {
        var owner = Owner.Create(UserId, AnyCpf, Now);
        var other = Cpf.Create("11144477735").Value!;

        owner.ChangeCpf(other, Now.AddDays(1)).IsSuccess.Should().BeTrue();

        owner.Cpf.Should().Be(other);
        owner.UpdatedAt.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public void ChangeCpf_Should_FailWithLocked_When_FirstPaymentHappened()
    {
        var owner = Owner.Create(UserId, AnyCpf, Now);
        owner.LinkStripeCustomer("cus_123", Now);

        var result = owner.ChangeCpf(Cpf.Create("11144477735").Value!, Now.AddDays(1));

        result.FirstError!.Code.Should().Be("OWNER_CPF_LOCKED");
        owner.Cpf.Should().Be(AnyCpf);
        owner.CanChangeCpf.Should().BeFalse();
    }

    [Fact]
    public void Deactivate_Should_MarkInactiveWithDate_When_Active()
    {
        var owner = Owner.Create(UserId, AnyCpf, Now);

        owner.Deactivate(Now.AddDays(1));

        owner.IsActive.Should().BeFalse();
        owner.DeactivatedAt.Should().Be(Now.AddDays(1));
        owner.UpdatedAt.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public void Deactivate_Should_KeepFirstDate_When_AlreadyInactive()
    {
        var owner = Owner.Create(UserId, AnyCpf, Now);
        owner.Deactivate(Now.AddDays(1));

        owner.Deactivate(Now.AddDays(2));

        owner.DeactivatedAt.Should().Be(Now.AddDays(1));
    }

    [Fact]
    public void Reactivate_Should_ClearDeactivation_When_Inactive()
    {
        var owner = Owner.Create(UserId, AnyCpf, Now);
        owner.Deactivate(Now.AddDays(1));

        owner.Reactivate(Now.AddDays(2));

        owner.IsActive.Should().BeTrue();
        owner.DeactivatedAt.Should().BeNull();
        owner.UpdatedAt.Should().Be(Now.AddDays(2));
    }

    [Fact]
    public void ChangeCpf_Should_NotTouchUpdatedAt_When_CpfIsTheSame()
    {
        var owner = Owner.Create(UserId, AnyCpf, Now);

        owner.ChangeCpf(AnyCpf, Now.AddDays(1)).IsSuccess.Should().BeTrue();

        owner.UpdatedAt.Should().Be(Now);
    }

    // ----- suspensão e liberação do CPF (admin) -----

    [Fact]
    public void Suspend_Should_MarkSuspended_When_Called()
    {
        var owner = Owner.Create(UserId, AnyCpf, Now);

        owner.Suspend(Now.AddDays(1));

        owner.IsSuspended.Should().BeTrue();
        owner.SuspendedAt.Should().Be(Now.AddDays(1));
        owner.IsActive.Should().BeTrue("suspender não é inativar");
    }

    [Fact]
    public void ReleaseCpf_Should_Fail_When_OwnerIsNotSuspended()
    {
        var owner = Owner.Create(UserId, AnyCpf, Now);

        owner.ReleaseCpf(Now).FirstError!.Code.Should().Be("OWNER_SUSPENSION_REQUIRED");
        owner.Cpf.Should().Be(AnyCpf);
    }

    [Fact]
    public void ReleaseCpf_Should_ClearTheCpf_When_OwnerIsSuspended()
    {
        var owner = Owner.Create(UserId, AnyCpf, Now);
        owner.Suspend(Now);

        owner.ReleaseCpf(Now.AddDays(1)).IsSuccess.Should().BeTrue();

        owner.Cpf.Should().BeNull();
    }

    [Fact]
    public void LiftSuspension_Should_Fail_When_CpfWasReleased()
    {
        // A conta era indevida, e o CPF pode já estar com o dono de verdade.
        var owner = Owner.Create(UserId, AnyCpf, Now);
        owner.Suspend(Now);
        owner.ReleaseCpf(Now);

        owner.LiftSuspension(Now.AddDays(1)).FirstError!.Code.Should().Be("OWNER_CPF_RELEASED");
        owner.IsSuspended.Should().BeTrue();
    }

    [Fact]
    public void LiftSuspension_Should_ClearSuspension_When_CpfIsKept()
    {
        var owner = Owner.Create(UserId, AnyCpf, Now);
        owner.Suspend(Now);

        owner.LiftSuspension(Now.AddDays(1)).IsSuccess.Should().BeTrue();

        owner.IsSuspended.Should().BeFalse();
    }
}
