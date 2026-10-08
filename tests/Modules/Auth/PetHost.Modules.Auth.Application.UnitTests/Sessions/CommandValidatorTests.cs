using FluentAssertions;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Application.Sessions.CreateSession;
using PetHost.Modules.Auth.Domain.Users;
using Xunit;

namespace PetHost.Modules.Auth.Application.UnitTests.Sessions;

public sealed class CreateSessionCommandValidatorTests
{
    private readonly CreateSessionCommandValidator _sut = new();

    [Theory]
    [InlineData("owner")]
    [InlineData("host")]
    [InlineData("admin")]
    public void Validate_Should_Pass_When_RoleIsKnown(string role)
    {
        var result = _sut.Validate(new CreateSessionCommand("camila@exemplo.com", "senha", role));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_Fail_When_RoleIsUnknown()
    {
        var result = _sut.Validate(new CreateSessionCommand("camila@exemplo.com", "senha", "tutor"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Role");
    }

    [Fact]
    public void Validate_Should_NotEnforceMinimumPasswordLength_When_LoggingIn()
    {
        // No login a senha e conferida, nao cadastrada: exigir tamanho minimo
        // aqui vazaria a politica de senha e daria resposta diferente de
        // "credencial invalida".
        var result = _sut.Validate(new CreateSessionCommand("camila@exemplo.com", "x", "owner"));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_Fail_When_CredentialsAreMissing()
    {
        var result = _sut.Validate(new CreateSessionCommand(null, null, null));

        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.PropertyName).Should().Contain(["Email", "Password", "Role"]);
    }
}
