using FluentAssertions;
using PetHost.Modules.Auth.Domain.Users;
using Xunit;

namespace PetHost.Modules.Auth.Domain.UnitTests.Users;

public sealed class UserRoleValuesTests
{
    [Theory]
    [InlineData(UserRole.Owner, "owner")]
    [InlineData(UserRole.Host, "host")]
    [InlineData(UserRole.Admin, "admin")]
    public void ToWire_Should_MatchDataDictionary_When_RoleIsKnown(UserRole role, string expected)
    {
        UserRoleValues.ToWire(role).Should().Be(expected);
    }

    [Fact]
    public void ToWire_Should_Throw_When_RoleIsNotMapped()
    {
        var act = () => UserRoleValues.ToWire((UserRole)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("owner", UserRole.Owner)]
    [InlineData("OWNER", UserRole.Owner)]
    [InlineData("  Host  ", UserRole.Host)]
    [InlineData("admin", UserRole.Admin)]
    public void TryParse_Should_Succeed_When_ValueIsKnownIgnoringCase(string input, UserRole expected)
    {
        UserRoleValues.TryParse(input, out var role).Should().BeTrue();
        role.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("tutor")]
    [InlineData("anfitriao")]
    [InlineData("superadmin")]
    public void TryParse_Should_Fail_When_ValueIsUnknown(string? input)
    {
        UserRoleValues.TryParse(input, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(UserRole.Owner)]
    [InlineData(UserRole.Host)]
    [InlineData(UserRole.Admin)]
    public void ToWireAndTryParse_Should_RoundTrip_When_RoleIsKnown(UserRole role)
    {
        UserRoleValues.TryParse(UserRoleValues.ToWire(role), out var parsed).Should().BeTrue();

        parsed.Should().Be(role);
    }
}
