using FluentAssertions;
using PetHost.Modules.Auth.Domain.Users;
using Xunit;

namespace PetHost.Modules.Auth.Domain.UnitTests.Users;

public sealed class PasswordTests
{
    [Theory]
    [InlineData("Nova@Senha123")]
    [InlineData("Ab1!abcd")]
    [InlineData("Çãoçãoç1#")]
    [InlineData("  Espaço Conta 1! ")]
    public void Create_Should_Succeed_When_PasswordIsStrong(string value)
    {
        var result = Password.Create(value);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Value.Should().Be(value, "senha não é aparada: espaço faz parte dela");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Create_Should_FailWithRequired_When_ValueIsMissing(string? value)
    {
        var result = Password.Create(value);

        result.Errors.Should().ContainSingle()
            .Which.Message.Should().Be("Password is required.");
    }

    [Theory]
    [InlineData("Ab1!abc", "at least 8 characters")]
    [InlineData("nova@senha123", "uppercase")]
    [InlineData("NOVA@SENHA123", "lowercase")]
    [InlineData("Nova@Senhaabc", "number")]
    [InlineData("NovaSenha1234", "special character")]
    [InlineData("Nova Senha 123", "special character")]
    public void Create_Should_Fail_When_ARuleIsMissing(string value, string expectedMessagePart)
    {
        var result = Password.Create(value);

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle()
            .Which.Message.Should().Contain(expectedMessagePart);
    }

    [Fact]
    public void Create_Should_ReturnEveryBrokenRule_When_PasswordIsWeak()
    {
        // "abc": curta, sem maiúscula, sem número, sem especial.
        var result = Password.Create("abc");

        result.Errors.Should().HaveCount(4);
        result.Errors!.Should().OnlyContain(e => e.Field == "password");
    }

    [Fact]
    public void Create_Should_Fail_When_PasswordExceedsMaxLength()
    {
        var result = Password.Create("Aa1!" + new string('a', Password.MaxLength));

        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("at most");
    }

    [Fact]
    public void ToString_Should_HideTheValue_When_Logged()
    {
        Password.Create("Nova@Senha123").Value!.ToString().Should().Be("***");
    }
}
