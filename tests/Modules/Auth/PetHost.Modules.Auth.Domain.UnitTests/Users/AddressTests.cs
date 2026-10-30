using FluentAssertions;
using PetHost.Modules.Auth.Domain.Users;
using Xunit;

namespace PetHost.Modules.Auth.Domain.UnitTests.Users;

public sealed class ZipCodeTests
{
    [Theory]
    [InlineData("87020-000", "87020000")]
    [InlineData("87020000", "87020000")]
    [InlineData(" 87.020-000 ", "87020000")]
    public void Create_Should_KeepOnlyDigits_When_InputIsFormatted(string input, string expected)
    {
        ZipCode.Create(input).Value!.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("8702-000")]
    [InlineData("870200000")]
    [InlineData("87O20-000")]
    [InlineData("00000-000")]
    public void Create_Should_Fail_When_ValueIsNotAZipCode(string? input)
    {
        ZipCode.Create(input).FirstError!.Field.Should().Be("address.zipCode");
    }
}

public sealed class AddressTests
{
    [Fact]
    public void Create_Should_NormalizeEveryField_When_InputIsValid()
    {
        var result = Address.Create(" 87020-000 ", " Rua das Flores ", " 120 ", " Apto 3 ", " Zona 7 ", " Maringá ", "pr");

        result.IsSuccess.Should().BeTrue();
        var address = result.Value!;
        address.ZipCode.Value.Should().Be("87020000");
        address.Street.Should().Be("Rua das Flores");
        address.Number.Should().Be("120");
        address.Complement.Should().Be("Apto 3");
        address.Neighborhood.Should().Be("Zona 7");
        address.City.Should().Be("Maringá");
        address.State.Value.Should().Be("PR");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Create_Should_LeaveComplementEmpty_When_NotInformed(string? complement)
    {
        Address.Create("87020000", "Rua A", "S/N", complement, "Zona 7", "Maringá", "PR")
            .Value!.Complement.Should().BeNull();
    }

    [Fact]
    public void Create_Should_ReturnEveryInvalidField_When_AddressIsEmpty()
    {
        var result = Address.Create(null, null, null, null, null, null, null);

        result.Errors!.Select(e => e.Field).Should().BeEquivalentTo(
            "address.zipCode",
            "address.street",
            "address.number",
            "address.neighborhood",
            "address.city",
            "address.state");
    }

    [Fact]
    public void Create_Should_Fail_When_FieldsExceedTheirColumns()
    {
        var result = Address.Create(
            "87020000",
            new string('a', Address.StreetMaxLength + 1),
            new string('1', Address.NumberMaxLength + 1),
            new string('c', Address.ComplementMaxLength + 1),
            new string('b', Address.NeighborhoodMaxLength + 1),
            new string('m', Address.CityMaxLength + 1),
            "PR");

        result.Errors!.Select(e => e.Field).Should().BeEquivalentTo(
            "address.street",
            "address.number",
            "address.complement",
            "address.neighborhood",
            "address.city");
    }

    [Fact]
    public void Equals_Should_CompareByValue_When_SameAddressAfterNormalization()
    {
        var a = Address.Create("87020-000", "Rua A", "1", null, "Zona 7", "Maringá", "PR").Value;
        var b = Address.Create("87020000", " Rua A ", "1", "  ", "Zona 7", "Maringá", "pr").Value;

        a.Should().Be(b);
    }
}
