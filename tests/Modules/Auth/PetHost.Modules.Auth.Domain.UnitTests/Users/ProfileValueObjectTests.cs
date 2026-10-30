using FluentAssertions;
using PetHost.Modules.Auth.Domain.Users;
using Xunit;

namespace PetHost.Modules.Auth.Domain.UnitTests.Users;

public sealed class FullNameTests
{
    [Fact]
    public void Create_Should_Trim_When_ValueHasSurroundingSpaces()
    {
        FullName.Create("  Camila Souza  ").Value!.Value.Should().Be("Camila Souza");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    public void Create_Should_Fail_When_ValueIsMissing(string? value)
    {
        FullName.Create(value).FirstError!.Field.Should().Be("fullName");
    }

    [Fact]
    public void Create_Should_Fail_When_ValueExceedsMaxLength()
    {
        var result = FullName.Create(new string('a', FullName.MaxLength + 1));

        result.FirstError!.Message.Should().Contain("at most");
    }

    [Fact]
    public void Equals_Should_CompareByValue_When_SameNameAfterTrim()
    {
        FullName.Create("Camila").Value.Should().Be(FullName.Create(" Camila ").Value);
    }
}

public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData("(44) 99999-0000", "44999990000")]
    [InlineData("44 99999-0000", "44999990000")]
    [InlineData("+55 44 99999-0000", "5544999990000")]
    [InlineData("44 3222-1111", "4432221111")]
    [InlineData("44.3222.1111", "4432221111")]
    public void Create_Should_KeepOnlyDigits_When_InputIsFormatted(string input, string expected)
    {
        PhoneNumber.Create(input).Value!.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("9999-0000")]
    [InlineData("12345678901234")]
    [InlineData("44 9999A-0000")]
    public void Create_Should_Fail_When_ValueIsNotAPhone(string? input)
    {
        PhoneNumber.Create(input).FirstError!.Field.Should().Be("phone");
    }
}

public sealed class AvatarUrlTests
{
    [Theory]
    [InlineData("https://cdn.pethost.com/avatars/1.png")]
    [InlineData("http://localhost:9000/a.jpg")]
    public void Create_Should_Succeed_When_UrlIsAbsoluteWeb(string input)
    {
        AvatarUrl.Create(input).Value!.Value.Should().Be(input);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("avatars/1.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("ftp://files.pethost.com/a.png")]
    public void Create_Should_Fail_When_UrlIsNotAbsoluteWeb(string? input)
    {
        AvatarUrl.Create(input).FirstError!.Field.Should().Be("avatarUrl");
    }

    [Fact]
    public void Create_Should_Fail_When_UrlExceedsMaxLength()
    {
        var url = "https://cdn.pethost.com/" + new string('a', AvatarUrl.MaxLength);

        AvatarUrl.Create(url).FirstError!.Message.Should().Contain("at most");
    }
}

public sealed class StateCodeTests
{
    [Theory]
    [InlineData("PR", "PR")]
    [InlineData("pr", "PR")]
    [InlineData(" sp ", "SP")]
    [InlineData("DF", "DF")]
    public void Create_Should_Normalize_When_StateExists(string input, string expected)
    {
        StateCode.Create(input).Value!.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("XX")]
    [InlineData("PRR")]
    [InlineData("Paraná")]
    public void Create_Should_Fail_When_StateDoesNotExist(string? input)
    {
        StateCode.Create(input).FirstError!.Field.Should().Be("state");
    }
}
