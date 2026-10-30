using FluentAssertions;
using PetHost.Modules.Hosts.Domain.Hosts;
using Xunit;

namespace PetHost.Modules.Hosts.Domain.UnitTests.Hosts;

public sealed class CpfTests
{
    [Theory]
    [InlineData("529.982.247-25")]
    [InlineData("52998224725")]
    public void Create_Should_KeepOnlyDigits_When_CpfIsValid(string input)
    {
        // Act
        var result = Cpf.Create(input);

        // Assert
        result.Value!.Value.Should().Be("52998224725");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("529.982.247-26")]
    [InlineData("111.111.111-11")]
    [InlineData("5299822472")]
    [InlineData("529a982.247-25")]
    public void Create_Should_FailWithCpfInvalid_When_CpfIsNotValid(string? input)
    {
        // Act
        var result = Cpf.Create(input);

        // Assert
        result.FirstError!.Field.Should().Be("cpf");
    }

    [Fact]
    public void ToString_Should_MaskTheCpf_When_Called()
    {
        // Act / Assert
        Cpf.Create("52998224725").Value!.ToString().Should().Be("***.***.247-25");
    }
}
