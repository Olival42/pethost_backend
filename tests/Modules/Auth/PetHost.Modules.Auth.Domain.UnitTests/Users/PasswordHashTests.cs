using FluentAssertions;
using PetHost.Modules.Auth.Domain.Users;
using Xunit;

namespace PetHost.Modules.Auth.Domain.UnitTests.Users;

public sealed class PasswordHashTests
{
    [Fact]
    public void FromHash_Should_Succeed_When_HashIsPresent()
    {
        var result = PasswordHash.FromHash("$argon2id$v=19$m=19456,t=2,p=1$c2FsdA$aGFzaA");

        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromHash_Should_Fail_When_HashIsMissing(string? hash)
    {
        var result = PasswordHash.FromHash(hash);

        result.IsFailure.Should().BeTrue();
        result.FirstError!.Code.Should().Be("AUTH_PASSWORD_HASH_INVALID");
    }

    [Fact]
    public void FromHash_Should_Fail_When_HashExceedsColumnLength()
    {
        var result = PasswordHash.FromHash(new string('x', PasswordHash.MaxLength + 1));

        result.IsFailure.Should().BeTrue();
        result.FirstError!.Code.Should().Be("AUTH_PASSWORD_HASH_INVALID");
    }

    [Fact]
    public void ToString_Should_NotLeakHash_When_Logged()
    {
        var hash = PasswordHash.FromHash("$argon2id$v=19$m=19456,t=2,p=1$c2FsdA$c2VncmVkbw").Value!;

        hash.ToString().Should().Be("***");
        hash.ToString().Should().NotContain("c2VncmVkbw");
    }
}
