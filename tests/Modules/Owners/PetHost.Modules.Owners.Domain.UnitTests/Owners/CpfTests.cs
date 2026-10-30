using FluentAssertions;
using PetHost.Modules.Owners.Domain.Owners;
using Xunit;

namespace PetHost.Modules.Owners.Domain.UnitTests.Owners;

public sealed class CpfTests
{
    [Theory]
    [InlineData("529.982.247-25", "52998224725")]
    [InlineData("52998224725", "52998224725")]
    [InlineData(" 111.444.777-35 ", "11144477735")]
    public void Create_Should_KeepOnlyDigits_When_CpfIsValid(string input, string expected)
    {
        Cpf.Create(input).Value!.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("529.982.247-24")]
    [InlineData("529.982.247-35")]
    [InlineData("111.111.111-11")]
    [InlineData("000.000.000-00")]
    [InlineData("5299822472")]
    [InlineData("529982247255")]
    [InlineData("529.982.247-2A")]
    public void Create_Should_Fail_When_CpfIsInvalid(string? input)
    {
        Cpf.Create(input).FirstError!.Field.Should().Be("cpf");
    }

    [Fact]
    public void ToString_Should_MaskTheCpf_When_Logged()
    {
        // LGPD: o CPF inteiro não pode vazar em log por descuido.
        Cpf.Create("529.982.247-25").Value!.ToString().Should().Be("***.***.247-25");
    }
}
