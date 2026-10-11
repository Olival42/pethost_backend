using FluentAssertions;
using PetHost.Modules.Hosts.Domain.Hosts;
using Xunit;

namespace PetHost.Modules.Hosts.Domain.UnitTests.Hosts;

public sealed class CompanyDetailsTests
{
    [Fact]
    public void Create_Should_NormalizeEverything_When_DataIsValid()
    {
        // Act
        var result = CompanyDetails.Create("11.222.333/0001-81", " Pet Feliz LTDA ", " Pet Feliz ", "87013-000", "Av. Brasil", "1500", null, "Centro", "Maringá", "pr");

        // Assert
        var company = result.Value!;
        company.Cnpj.Value.Should().Be("11222333000181");
        company.LegalName.Should().Be("Pet Feliz LTDA");
        company.TradeName.Should().Be("Pet Feliz");
        company.Address.ZipCode.Should().Be("87013000");
        company.Address.State.Should().Be("PR");
    }

    [Fact]
    public void Create_Should_ReturnEveryInvalidField_When_NothingIsSent()
    {
        // Act
        var result = CompanyDetails.Create(null, null, null, null, null, null, null, null, null, null);

        // Assert
        result.Errors!.Select(e => e.Field).Should().Contain(
            ["cnpj", "legalName", "tradeName", "companyAddress.zipCode", "companyAddress.street", "companyAddress.number",
             "companyAddress.neighborhood", "companyAddress.city", "companyAddress.state"]);
    }

    [Fact]
    public void Equality_Should_CompareAllTheData_When_Compared()
    {
        // Arrange
        var a = CompanyDetails.Create("11222333000181", "Razão", "Fantasia", "87013000", "Rua", "1", null, "Centro", "Maringá", "PR").Value!;
        var b = CompanyDetails.Create("11.222.333/0001-81", "Razão", "Fantasia", "87013-000", "Rua", "1", null, "Centro", "Maringá", "PR").Value!;
        var c = CompanyDetails.Create("11222333000181", "Razão", "Outro nome", "87013000", "Rua", "1", null, "Centro", "Maringá", "PR").Value!;

        // Act / Assert
        a.Should().Be(b);
        a.Should().NotBe(c);
    }
}
