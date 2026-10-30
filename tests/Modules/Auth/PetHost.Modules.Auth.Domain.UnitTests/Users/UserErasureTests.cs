using FluentAssertions;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Auth.Domain.UnitTests.Users;

/// <summary>Exclusão da conta a pedido do titular (LGPD): anonimiza e não volta.</summary>
public sealed class UserErasureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    private static PasswordHash Unusable => PasswordHash.FromHash("$argon2id$v=19$m=65536,t=3,p=1$dW51c2FibGU$aGFzaA").Value!;

    [Fact]
    public void Erase_Should_AnonymizeThePersonalData_When_Called()
    {
        var user = new UserBuilder().WithFullName("Camila Souza").WithEmail("camila@exemplo.com").Build();

        user.Erase(Unusable, Now).IsSuccess.Should().BeTrue();

        user.FullName.Value.Should().Be(User.ErasedFullName);
        user.Email.Value.Should().Be($"excluida-{user.Id.Value:N}@{User.ErasedEmailDomain}");
        user.PasswordHash.Should().Be(Unusable);
        user.Phone.Should().BeNull();
        user.BirthDate.Should().BeNull();
        user.Address.Should().BeNull();
        user.AvatarUrl.Should().BeNull();
        user.IsErased.Should().BeTrue();
        user.ErasedAt.Should().Be(Now);
        user.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Erase_Should_Deactivate_When_AccountIsActive()
    {
        var user = new UserBuilder().Build();

        user.Erase(Unusable, Now);

        user.IsActive.Should().BeFalse();
        user.DeactivatedAt.Should().Be(Now);
    }

    [Fact]
    public void Erase_Should_KeepTheFirstDeactivation_When_AccountWasAlreadyInactive()
    {
        var user = new UserBuilder().Build();
        user.Deactivate(Now.AddDays(-3));

        user.Erase(Unusable, Now);

        user.DeactivatedAt.Should().Be(Now.AddDays(-3));
    }

    [Fact]
    public void Erase_Should_KeepTheFirstErasure_When_CalledTwice()
    {
        var user = new UserBuilder().Build();
        user.Erase(Unusable, Now);

        user.Erase(Unusable, Now.AddDays(1)).IsSuccess.Should().BeTrue();

        user.ErasedAt.Should().Be(Now);
    }

    [Fact]
    public void Erase_Should_GiveEachAccountItsOwnEmail_When_TheSamePersonErasesTwoAccounts()
    {
        var owner = new UserBuilder().WithEmail("cida@exemplo.com").WithRole(UserRole.Owner).Build();
        var host = new UserBuilder().WithEmail("cida@exemplo.com").WithRole(UserRole.Host).Build();

        owner.Erase(Unusable, Now);
        host.Erase(Unusable, Now);

        owner.Email.Should().NotBe(host.Email);
    }

    [Fact]
    public void Erase_Should_Fail_When_AccountIsAdmin()
    {
        var admin = new UserBuilder().WithRole(UserRole.Admin).Build();

        admin.Erase(Unusable, Now).FirstError!.Code.Should().Be("AUTH_ADMIN_ERASURE_FORBIDDEN");
        admin.IsErased.Should().BeFalse();
    }

    [Fact]
    public void Reactivate_Should_Fail_When_AccountWasErased()
    {
        var user = new UserBuilder().Build();
        user.Erase(Unusable, Now);

        user.Reactivate(Now.AddDays(1)).FirstError!.Code.Should().Be("AUTH_ACCOUNT_ERASED");
        user.IsActive.Should().BeFalse();
    }
}
