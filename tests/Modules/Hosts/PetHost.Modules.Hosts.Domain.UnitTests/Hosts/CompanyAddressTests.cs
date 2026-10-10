using FluentAssertions;
using PetHost.Modules.Hosts.Domain.Hosts;
using Xunit;

namespace PetHost.Modules.Hosts.Domain.UnitTests.Hosts;

public sealed class CompanyAddressTests
{
    [Fact]
    public void Create_Should_NormalizeEveryField_When_AddressIsValid()
    {
        // Act
        var result = CompanyAddress.Create(" 87020-000 ", " Av. Brasil ", " 1500 ", "  ", " Centro ", " Maringá ", " pr ");

        // Assert
        var address = result.Value!;
        address.ZipCode.Should().Be("87020000");
        address.Street.Should().Be("Av. Brasil");
        address.Number.Should().Be("1500");
        address.Complement.Should().BeNull("complemento vazio vira nulo");
        address.Neighborhood.Should().Be("Centro");
        address.City.Should().Be("Maringá");
        address.State.Should().Be("PR");
    }

    [Fact]
    public void Create_Should_ReturnEveryInvalidField_When_ManyAreWrong()
    {
        // Act
        var result = CompanyAddress.Create("123", "", null, new string('x', 61), "", " ", "XX");

        // Assert
        result.Errors!.Select(e => e.Field).Should().BeEquivalentTo(
        [
            "companyAddress.zipCode",
            "companyAddress.street",
            "companyAddress.number",
            "companyAddress.complement",
            "companyAddress.neighborhood",
            "companyAddress.city",
            "companyAddress.state",
        ]);
    }

    [Theory]
    [InlineData("00000-000")]
    [InlineData("87020-00a")]
    public void Create_Should_RejectZipCode_When_ItIsNotARealCep(string zipCode)
    {
        // Act
        var result = CompanyAddress.Create(zipCode, "Rua", "1", null, "Centro", "Maringá", "PR");

        // Assert
        result.Errors!.Should().ContainSingle(e => e.Field == "companyAddress.zipCode");
    }

    [Fact]
    public void Create_Should_RejectTooLongFields_When_TheyPassTheLimits()
    {
        // Act
        var result = CompanyAddress.Create(
            "87020000",
            new string('r', 121),
            new string('1', 11),
            null,
            new string('b', 81),
            new string('c', 81),
            "PR");

        // Assert
        result.Errors!.Select(e => e.Field).Should().BeEquivalentTo(
            ["companyAddress.street", "companyAddress.number", "companyAddress.neighborhood", "companyAddress.city"]);
    }
}
