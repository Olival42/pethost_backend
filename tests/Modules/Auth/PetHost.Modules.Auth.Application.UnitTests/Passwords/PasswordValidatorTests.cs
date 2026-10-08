using FluentAssertions;
using PetHost.Modules.Auth.Application.Passwords.ForgotPassword;
using PetHost.Modules.Auth.Application.Passwords.ResetPassword;
using PetHost.Modules.Auth.Domain.Users;
using Xunit;

namespace PetHost.Modules.Auth.Application.UnitTests.Passwords;

public sealed class ResetPasswordCommandValidatorTests
{
    private readonly ResetPasswordCommandValidator _sut = new();

    [Theory]
    [InlineData("Nova@Senha123")]
    [InlineData("Ab1!abcd")]
    [InlineData("Çãoçãoç1#")]
    public void Validate_Should_Pass_When_PasswordIsStrong(string password)
    {
        var result = _sut.Validate(new ResetPasswordCommand("token", password));

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("Ab1!abc", "at least 8 characters")]
    [InlineData("nova@senha123", "uppercase")]
    [InlineData("NOVA@SENHA123", "lowercase")]
    [InlineData("Nova@Senhaabc", "number")]
    [InlineData("NovaSenha1234", "special character")]
    [InlineData("Nova Senha 123", "special character")]
    public void Validate_Should_Fail_When_PasswordMissesARule(string password, string expectedMessagePart)
    {
        var result = _sut.Validate(new ResetPasswordCommand("token", password));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.PropertyName == "NewPassword" && e.ErrorMessage.Contains(expectedMessagePart));
    }

    [Fact]
    public void Validate_Should_ListEveryMissingRule_When_PasswordIsWeak()
    {
        // O cliente recebe tudo o que falta de uma vez, não um "senha fraca" genérico.
        var result = _sut.Validate(new ResetPasswordCommand("token", "abc"));

        result.Errors.Where(e => e.PropertyName == "NewPassword").Should().HaveCount(4);
    }

    [Fact]
    public void Validate_Should_Fail_When_PasswordExceedsPolicy()
    {
        var password = "Aa1!" + new string('a', Password.MaxLength);

        var result = _sut.Validate(new ResetPasswordCommand("token", password));

        result.Errors.Should().Contain(e => e.PropertyName == "NewPassword" && e.ErrorMessage.Contains("at most"));
    }

    [Fact]
    public void Validate_Should_AccumulateEveryFailure_When_CommandIsEmpty()
    {
        var result = _sut.Validate(new ResetPasswordCommand(null, null));

        result.Errors.Select(e => e.PropertyName).Should().Contain(["Token", "NewPassword"]);
    }
}

public sealed class ForgotPasswordCommandValidatorTests
{
    private readonly ForgotPasswordCommandValidator _sut = new();

    [Fact]
    public void Validate_Should_Pass_When_CommandIsComplete()
    {
        _sut.Validate(new ForgotPasswordCommand("camila@exemplo.com", "owner")).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_AccumulateEveryFailure_When_CommandIsEmpty()
    {
        var result = _sut.Validate(new ForgotPasswordCommand(null, null));

        result.Errors.Select(e => e.PropertyName).Should().Contain(["Email", "Role"]);
    }

    [Fact]
    public void Validate_Should_Fail_When_RoleIsUnknown()
    {
        var result = _sut.Validate(new ForgotPasswordCommand("camila@exemplo.com", "gerente"));

        result.Errors.Should().Contain(e => e.PropertyName == "Role");
    }
}
