using FluentAssertions;
using PetHost.Modules.Hosts.Domain.Hosts;
using Xunit;

namespace PetHost.Modules.Hosts.Domain.UnitTests.Hosts;

public sealed class CnpjTests
{
    [Theory]
    [InlineData("11.222.333/0001-81", "11222333000181")]
    [InlineData("11222333000181", "11222333000181")]
    [InlineData("12.ABC.345/01DE-35", "12ABC34501DE35")]
    [InlineData("12abc34501de35", "12ABC34501DE35")]
    public void Create_Should_KeepOnlyCharactersInUpperCase_When_CnpjIsValid(string input, string expected)
    {
        // Act
        var result = Cnpj.Create(input);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("11.222.333/0001-82")]
    [InlineData("12.ABC.345/01DE-36")]
    [InlineData("11111111111111")]
    [InlineData("1122233300018")]
    [InlineData("12ABC34501DEAB")]
    [InlineData("11.222.333/0001#81")]
    public void Create_Should_FailWithCnpjInvalid_When_CnpjIsNotValid(string? input)
    {
        // Act
        var result = Cnpj.Create(input);

        // Assert
        result.FirstError!.Field.Should().Be("cnpj");
    }

    [Fact]
    public void Equals_Should_BeTrue_When_SameCnpjWithAndWithoutMask()
    {
        // Act / Assert
        Cnpj.Create("11.222.333/0001-81").Value.Should().Be(Cnpj.Create("11222333000181").Value);
    }
}
