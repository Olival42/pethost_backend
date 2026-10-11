using FluentAssertions;
using PetHost.Modules.Hosts.Domain.Hosts;
using Xunit;

namespace PetHost.Modules.Hosts.Domain.UnitTests.Hosts;

public sealed class HostTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid ApplicationId = Guid.CreateVersion7();
    private static readonly Guid AdminId = Guid.CreateVersion7();

    private static Cpf AnyCpf => Cpf.Create("52998224725").Value!;
    private static Cnpj AnyCnpj => Cnpj.Create("11222333000181").Value!;
    private static CompanyAddress AnyAddress => CompanyAddress.Create("87020000", "Av. Brasil", "1500", null, "Centro", "Maringá", "PR").Value!;

    [Fact]
    public void CreateIndividual_Should_CreateActiveHostWithoutCompanyData_When_Called()
    {
        // Act
        var host = Host.CreateIndividual(UserId, AnyCpf, ApplicationId, AdminId, Now);

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
        host.ApplicationId.Should().Be(ApplicationId);
        host.VerifiedBy.Should().Be(AdminId);
        host.VerifiedAt.Should().Be(Now);
        host.StripePayoutsEnabled.Should().BeFalse("o Stripe ainda não liberou repasses");
    }

    [Fact]
    public void LinkStripeAccount_Should_KeepTheConnectedAccount_When_Called()
    {
        // Arrange
        var host = Host.CreateIndividual(UserId, AnyCpf, ApplicationId, AdminId, Now);
        var later = Now.AddDays(1);

        // Act
        host.LinkStripeAccount(" acct_1PetHostDemo ", later);

        // Assert
        host.StripeAccountId.Should().Be("acct_1PetHostDemo");
        host.UpdatedAt.Should().Be(later);
    }

    [Fact]
    public void LinkStripeAccount_Should_Throw_When_IdIsTooLong()
    {
        // Arrange
        var host = Host.CreateIndividual(UserId, AnyCpf, ApplicationId, AdminId, Now);

        // Act
        var act = () => host.LinkStripeAccount(new string('a', Host.StripeAccountIdMaxLength + 1), Now);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateIndividual_Should_Throw_When_ApplicationIsEmpty()
    {
        // Act
        var act = () => Host.CreateIndividual(UserId, AnyCpf, Guid.Empty, AdminId, Now);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("applicationId");
    }

    [Fact]
    public void CreateIndividual_Should_Throw_When_AdminIsEmpty()
    {
        // Act
        var act = () => Host.CreateIndividual(UserId, AnyCpf, ApplicationId, Guid.Empty, Now);

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("verifiedBy");
    }

    [Fact]
    public void CreateCompany_Should_KeepCompanyDataAndRepresentativeCpf_When_DataIsValid()
    {
        // Act
        var result = Host.CreateCompany(UserId, AnyCpf, AnyCnpj, " Pet Feliz Hospedagem LTDA ", " Pet Feliz ", AnyAddress, ApplicationId, AdminId, Now);

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
        var result = Host.CreateCompany(UserId, AnyCpf, AnyCnpj, " ", null, AnyAddress, ApplicationId, AdminId, Now);

        // Assert
        result.Errors!.Select(e => e.Field).Should().BeEquivalentTo(["legalName", "tradeName"]);
    }

    [Fact]
    public void CreateCompany_Should_RejectNames_When_TheyAreTooLong()
    {
        // Act
        var result = Host.CreateCompany(
            UserId, AnyCpf, AnyCnpj, new string('l', Host.LegalNameMaxLength + 1), new string('t', Host.TradeNameMaxLength + 1), AnyAddress, ApplicationId, AdminId, Now);

        // Assert
        result.Errors!.Select(e => e.Field).Should().BeEquivalentTo(["legalName", "tradeName"]);
    }

    [Fact]
    public void CreateIndividual_Should_Throw_When_AccountIsEmpty()
    {
        // Act
        var act = () => Host.CreateIndividual(Guid.Empty, AnyCpf, ApplicationId, AdminId, Now);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateCompany_Should_Throw_When_AccountIsEmpty()
    {
        // Act
        var act = () => Host.CreateCompany(Guid.Empty, AnyCpf, AnyCnpj, "Razão", "Fantasia", AnyAddress, ApplicationId, AdminId, Now);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(PersonType.Individual, "pf")]
    [InlineData(PersonType.Company, "pj")]
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
