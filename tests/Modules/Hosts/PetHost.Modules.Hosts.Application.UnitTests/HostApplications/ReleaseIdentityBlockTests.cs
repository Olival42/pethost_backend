using FluentAssertions;
using Moq;
using PetHost.Modules.Hosts.Application.HostApplications.ReleaseIdentityBlock;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Modules.Hosts.Domain.IdentityBlocks;
using PetHost.Shared.Contracts.Audit;
using Xunit;
using static PetHost.Modules.Hosts.Application.UnitTests.HostApplications.HostsTestData;

namespace PetHost.Modules.Hosts.Application.UnitTests.HostApplications;

/// <summary>O admin libera um CPF bloqueado (o bloqueio pegou o dono de verdade).</summary>
public sealed class ReleaseIdentityBlockTests
{
    private readonly Mock<IIdentityBlockRepository> _identityBlocks = new();
    private readonly Mock<IHostsUnitOfWork> _unitOfWork = UnitOfWork();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly IdentityBlock _block = IdentityBlock.Create(FingerprintOf(CpfText), IdentityBlockReason.Fraud, Guid.CreateVersion7(), Now);

    private ReleaseIdentityBlockCommandHandler Handler() => new(_identityBlocks.Object, Fingerprint(), _unitOfWork.Object, _auditTrail.Object);

    [Fact]
    public async Task HandleAsync_Should_RemoveTheBlockAndRecordTheMaskedCpf_When_TheCpfIsBlocked()
    {
        // Arrange
        _identityBlocks.Setup(b => b.FindAsync(FingerprintOf(CpfText), It.IsAny<CancellationToken>())).ReturnsAsync(_block);

        // Act
        var result = await Handler().HandleAsync(new ReleaseIdentityBlockCommand(AdminId, CpfText, "Documento conferido pelo suporte"), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _identityBlocks.Verify(b => b.Remove(_block), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(
            t => t.RecordAsync(
                It.Is<AuditRecord>(r => r.Action == AuditActions.HostIdentityUnblocked && r.TargetId == _block.ApplicationId
                    && r.Reason == "Documento conferido pelo suporte" && r.Details!["cpf"] == "***.***.247-25" && r.Details["blockReason"] == "fraud"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_TheCpfIsNotBlocked()
    {
        // Act
        var result = await Handler().HandleAsync(new ReleaseIdentityBlockCommand(AdminId, CpfText, "Teste"), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(HostsErrors.IdentityBlockNotFound);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Validator_Should_RequireAValidCpfAndAReason_When_Missing()
    {
        // Act
        var result = new ReleaseIdentityBlockCommandValidator().Validate(new ReleaseIdentityBlockCommand(AdminId, "111.111.111-11", " "));

        // Assert
        result.Errors.Select(e => e.PropertyName).Should().Contain(["Cpf", "Reason"]);
    }
}
