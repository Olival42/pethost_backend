using FluentAssertions;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Kernel.Errors;
using Xunit;

namespace PetHost.Modules.Auth.Domain.UnitTests.Users;

public sealed class EmailTests
{
    [Theory]
    [InlineData("camila@exemplo.com", "camila@exemplo.com")]
    [InlineData("  camila@exemplo.com  ", "camila@exemplo.com")]
    [InlineData("CAMILA@EXEMPLO.COM", "camila@exemplo.com")]
    [InlineData("Camila.Souza@Exemplo.COM.BR", "camila.souza@exemplo.com.br")]
    public void Create_Should_Normalize_When_ValueHasSpacesOrUpperCase(string input, string expected)
    {
        var result = Email.Create(input);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Should_Fail_When_ValueIsMissing(string? input)
    {
        var result = Email.Create(input);

        result.IsFailure.Should().BeTrue();
        result.FirstError!.Code.Should().Be(ErrorCodes.Validation);
        result.FirstError.Field.Should().Be("email");
    }

    [Theory]
    [InlineData("camila")]
    [InlineData("camila@")]
    [InlineData("@exemplo.com")]
    [InlineData("camila@exemplo")]
    [InlineData("camila exemplo@teste.com")]
    [InlineData("camila@@exemplo.com")]
    public void Create_Should_Fail_When_FormatIsInvalid(string input)
    {
        var result = Email.Create(input);

        result.IsFailure.Should().BeTrue();
        result.FirstError!.Field.Should().Be("email");
    }

    [Fact]
    public void Create_Should_Fail_When_ValueExceedsMaxLength()
    {
        var local = new string('a', Email.MaxLength);

        var result = Email.Create($"{local}@exemplo.com");

        result.IsFailure.Should().BeTrue();
        result.FirstError!.Message.Should().Contain($"{Email.MaxLength}");
    }

    [Fact]
    public void Equals_Should_BeTrue_When_ValuesMatchAfterNormalization()
    {
        var first = Email.Create("Camila@Exemplo.com").Value!;
        var second = Email.Create("  camila@exemplo.COM ").Value!;

        first.Should().Be(second);
        (first == second).Should().BeTrue();
        first.GetHashCode().Should().Be(second.GetHashCode());
    }
}
