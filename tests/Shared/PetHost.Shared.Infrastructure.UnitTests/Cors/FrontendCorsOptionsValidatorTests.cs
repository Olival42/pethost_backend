using FluentAssertions;
using PetHost.Shared.Infrastructure.Cors;
using Xunit;

namespace PetHost.Shared.Infrastructure.UnitTests.Cors;

public sealed class FrontendCorsOptionsValidatorTests
{
    private readonly FrontendCorsOptionsValidator _sut = new();

    [Theory]
    [InlineData("http://localhost:3000")]
    [InlineData("https://app.pethost.com.br")]
    [InlineData("https://app.pethost.com.br/")]
    public void Validate_Should_Succeed_When_OriginIsSchemeHostAndPort(string origin)
    {
        // Act
        var result = _sut.Validate(null, Options(origin));

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void Validate_Should_Succeed_When_NoOriginIsConfigured()
    {
        // Act
        var result = _sut.Validate(null, new FrontendCorsOptions());

        // Assert
        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("*")]
    [InlineData("localhost:3000")]
    [InlineData("ftp://pethost.com.br")]
    [InlineData("https://app.pethost.com.br/login")]
    [InlineData("https://app.pethost.com.br?x=1")]
    [InlineData("https://*.pethost.com.br")]
    [InlineData("https://user@app.pethost.com.br")]
    [InlineData("")]
    public void Validate_Should_Fail_When_OriginIsNotAnHttpOrigin(string origin)
    {
        // Act
        var result = _sut.Validate(null, Options(origin));

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("AllowedOrigins");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(121)]
    public void Validate_Should_Fail_When_PreflightMaxAgeIsOutOfRange(int minutes)
    {
        // Arrange
        var options = Options("http://localhost:3000");
        options.PreflightMaxAgeMinutes = minutes;

        // Act
        var result = _sut.Validate(null, options);

        // Assert
        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("PreflightMaxAgeMinutes");
    }

    private static FrontendCorsOptions Options(string origin)
    {
        var options = new FrontendCorsOptions();
        options.AllowedOrigins.Add(origin);
        return options;
    }
}
