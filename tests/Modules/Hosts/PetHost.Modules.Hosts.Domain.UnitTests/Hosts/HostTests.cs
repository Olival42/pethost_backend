using FluentAssertions;
using PetHost.Modules.Hosts.Domain.Hosts;
using Xunit;

namespace PetHost.Modules.Hosts.Domain.UnitTests.Hosts;

public sealed class HostTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.CreateVersion7();

    private static Cpf AnyCpf => Cpf.Create("52998224725").Value!;
    private static Cnpj AnyCnpj => Cnpj.Create("11222333000181").Value!;
    private static CompanyAddress AnyAddress => CompanyAddress.Create("87020000", "Av. Brasil", "1500", null, "Centro", "Maringá", "PR").Value!;

    [Fact]
    public void CreateIndividual_Should_CreateActiveHostWithoutCompanyData_When_Called()
    {
        // Act
        var host = Host.CreateIndividual(UserId, AnyCpf, Now);

        // Assert
        host.Id.Value.Should().NotBe(Guid.Empty);
        host.Id.Value.Should().NotBe(UserId, "o anfitrião tem id próprio; a conta é só referência");
        host.UserId.Should().Be(UserId);
        host.PersonType.Should().Be(PersonType.Individual);
        host.Cpf.Should().Be(AnyCpf);
        host.Cnpj.Should().BeNull();
        host.LegalName.Should().BeNull();
        host.TradeName.Should().BeNull();
        host.CompanyAddress.Should().BeNull();
        host.StripeAccountId.Should().BeNull("a conta conectada só existe depois do onboarding");
        host.IsActive.Should().BeTrue();
        host.CreatedAt.Should().Be(Now);
        host.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void CreateCompany_Should_KeepCompanyDataAndRepresentativeCpf_When_DataIsValid()
    {
        // Act
        var result = Host.CreateCompany(UserId, AnyCpf, AnyCnpj, " Pet Feliz Hospedagem LTDA ", " Pet Feliz ", AnyAddress, Now);

        // Assert
        var host = result.Value!;
        host.PersonType.Should().Be(PersonType.Company);
        host.Cpf.Should().Be(AnyCpf, "na empresa o CPF é o do representante legal");
        host.Cnpj.Should().Be(AnyCnpj);
        host.LegalName.Should().Be("Pet Feliz Hospedagem LTDA");
        host.TradeName.Should().Be("Pet Feliz");
        host.CompanyAddress.Should().Be(AnyAddress);
        host.IsActive.Should().BeTrue();
    }

    [Fact]
    public void CreateCompany_Should_ReturnBothNameErrors_When_NamesAreMissing()
    {
        // Act
        var result = Host.CreateCompany(UserId, AnyCpf, AnyCnpj, " ", null, AnyAddress, Now);

        // Assert
        result.Errors!.Select(e => e.Field).Should().BeEquivalentTo(["legalName", "tradeName"]);
    }

    [Fact]
    public void CreateCompany_Should_RejectNames_When_TheyAreTooLong()
    {
        // Act
        var result = Host.CreateCompany(
            UserId, AnyCpf, AnyCnpj, new string('l', Host.LegalNameMaxLength + 1), new string('t', Host.TradeNameMaxLength + 1), AnyAddress, Now);

        // Assert
        result.Errors!.Select(e => e.Field).Should().BeEquivalentTo(["legalName", "tradeName"]);
    }

    [Fact]
    public void CreateIndividual_Should_Throw_When_AccountIsEmpty()
    {
        // Act
        var act = () => Host.CreateIndividual(Guid.Empty, AnyCpf, Now);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateCompany_Should_Throw_When_AccountIsEmpty()
    {
        // Act
        var act = () => Host.CreateCompany(Guid.Empty, AnyCpf, AnyCnpj, "Razão", "Fantasia", AnyAddress, Now);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(PersonType.Individual, "individual")]
    [InlineData(PersonType.Company, "company")]
    public void PersonTypeValues_Should_RoundTrip_When_Converted(PersonType type, string wire)
    {
        // Act / Assert
        PersonTypeValues.ToWire(type).Should().Be(wire);
        PersonTypeValues.FromWire(wire).Should().Be(type);
    }

    [Fact]
    public void PersonTypeValues_Should_Throw_When_ValueIsUnknown()
    {
        // Act
        var act = () => PersonTypeValues.FromWire("other");

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
