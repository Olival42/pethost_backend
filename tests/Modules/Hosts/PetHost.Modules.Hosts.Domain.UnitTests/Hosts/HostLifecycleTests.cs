using FluentAssertions;
using PetHost.Modules.Hosts.Domain.Hosts;
using Xunit;

namespace PetHost.Modules.Hosts.Domain.UnitTests.Hosts;

/// <summary>Inativar, suspender, liberar CPF/CNPJ e trocar o contato da empresa.</summary>
public sealed class HostLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Now.AddDays(1);

    private static Host Individual() =>
        Host.CreateIndividual(Guid.CreateVersion7(), Cpf.Create("52998224725").Value!, Guid.CreateVersion7(), Guid.CreateVersion7(), Now);

    private static Host Company() =>
        Host.CreateCompany(
            Guid.CreateVersion7(),
            Cpf.Create("52998224725").Value!,
            Cnpj.Create("11222333000181").Value!,
            "Pet Feliz Hospedagem LTDA",
            "Pet Feliz",
            CompanyAddress.Create("87013000", "Av. Brasil", "1500", null, "Centro", "Maringá", "PR").Value!,
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Now).Value!;

    [Fact]
    public void Deactivate_Should_BeIdempotent_When_CalledTwice()
    {
        // Arrange
        var host = Individual();

        // Act
        host.Deactivate(Now);
        host.Deactivate(Later);

        // Assert
        host.IsActive.Should().BeFalse();
        host.DeactivatedAt.Should().Be(Now, "vale a primeira inativação");
    }

    [Fact]
    public void Reactivate_Should_ClearTheDeactivation_When_Inactive()
    {
        // Arrange
        var host = Individual();
        host.Deactivate(Now);

        // Act
        host.Reactivate(Later);

        // Assert
        host.IsActive.Should().BeTrue();
        host.DeactivatedAt.Should().BeNull();
        host.UpdatedAt.Should().Be(Later);
    }

    [Fact]
    public void Suspend_Should_KeepTheFirstSuspension_When_CalledTwice()
    {
        // Arrange
        var host = Individual();

        // Act
        host.Suspend(Now);
        host.Suspend(Later);

        // Assert
        host.IsSuspended.Should().BeTrue();
        host.SuspendedAt.Should().Be(Now);
    }

    [Fact]
    public void LiftSuspension_Should_ClearIt_When_IdentityWasNotReleased()
    {
        // Arrange
        var host = Individual();
        host.Suspend(Now);

        // Act
        var result = host.LiftSuspension(Later);

        // Assert
        result.IsSuccess.Should().BeTrue();
        host.IsSuspended.Should().BeFalse();
    }

    [Fact]
    public void ReleaseIdentity_Should_RequireASuspension_When_HostIsNotSuspended()
    {
        // Act
        var result = Individual().ReleaseIdentity(Now);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_SUSPENSION_REQUIRED");
    }

    [Fact]
    public void ReleaseIdentity_Should_MarkTheReleaseAndBlockLifting_When_Suspended()
    {
        // Arrange
        var host = Individual();
        host.Suspend(Now);

        // Act
        var released = host.ReleaseIdentity(Later);
        var lifted = host.LiftSuspension(Later);

        // Assert
        released.IsSuccess.Should().BeTrue();
        host.IdentityReleasedAt.Should().Be(Later);
        host.Cpf.Value.Should().Be("52998224725", "o CPF fica como evidência");
        lifted.FirstError!.Code.Should().Be("HOST_IDENTITY_RELEASED");
        host.IsSuspended.Should().BeTrue();
    }

    [Fact]
    public void ReleaseIdentity_Should_KeepTheFirstRelease_When_CalledTwice()
    {
        // Arrange
        var host = Individual();
        host.Suspend(Now);
        host.ReleaseIdentity(Now);

        // Act
        var result = host.ReleaseIdentity(Later);

        // Assert
        result.IsSuccess.Should().BeTrue();
        host.IdentityReleasedAt.Should().Be(Now);
    }

    [Fact]
    public void ChangeCompanyContact_Should_UpdateTradeNameAndAddress_When_Company()
    {
        // Arrange
        var host = Company();
        var address = CompanyAddress.Create("87020000", "Rua Nova", "10", null, "Zona 7", "Maringá", "PR").Value!;

        // Act
        var result = host.ChangeCompanyContact(" Cantinho Pet Feliz ", address, Later);

        // Assert
        result.IsSuccess.Should().BeTrue();
        host.TradeName.Should().Be("Cantinho Pet Feliz");
        host.CompanyAddress.Should().Be(address);
        host.LegalName.Should().Be("Pet Feliz Hospedagem LTDA", "a razão social é identidade");
        host.UpdatedAt.Should().Be(Later);
    }

    [Fact]
    public void ChangeCompanyContact_Should_NotTouchUpdatedAt_When_NothingChanges()
    {
        // Arrange
        var host = Company();

        // Act
        host.ChangeCompanyContact("Pet Feliz", null, Later);

        // Assert
        host.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void ChangeCompanyContact_Should_RefuseAnEmptyTradeName_When_Sent()
    {
        // Act
        var result = Company().ChangeCompanyContact("  ", null, Later);

        // Assert
        result.FirstError!.Field.Should().Be("tradeName");
    }

    [Fact]
    public void ChangeCompanyContact_Should_ReturnNotACompany_When_HostIsAnIndividual()
    {
        // Act
        var result = Individual().ChangeCompanyContact("Pet Feliz", null, Later);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_NOT_A_COMPANY");
    }
}
