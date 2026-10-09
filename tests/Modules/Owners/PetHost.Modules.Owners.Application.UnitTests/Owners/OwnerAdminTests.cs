using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Application.Owners.SuspendOwner;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;
using Xunit;

namespace PetHost.Modules.Owners.Application.UnitTests.Owners;

/// <summary>Ações do admin: suspender, tirar a suspensão e liberar o CPF.</summary>
public sealed class OwnerAdminTests
{
    private static readonly Guid AdminId = Guid.CreateVersion7();
    private static readonly Guid UserId = Guid.CreateVersion7();

    private readonly Mock<IOwnerRepository> _ownerRepository = new();
    private readonly Mock<IOwnersUnitOfWork> _unitOfWork = new();
    private readonly Mock<IOwnerAccounts> _ownerAccounts = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly FakeTimeProvider _time = new(Accounts.Now);
    private readonly Owner _owner = Owner.Create(UserId, Cpf.Create("52998224725").Value!, Accounts.Now);

    public OwnerAdminTests()
    {
        _ownerRepository.Setup(r => r.GetByIdAsync(_owner.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_owner);
        _ownerAccounts
            .Setup(a => a.SuspendAsync(UserId, It.IsAny<string?>(), AdminId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _ownerAccounts
            .Setup(a => a.LiftSuspensionAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        _ownerAccounts
            .Setup(a => a.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Accounts.Summary(UserId, "Camila")]);
    }

    [Fact]
    public async Task Suspend_Should_SuspendOwnerAndAccountAndRecordTheReason_When_Called()
    {
        var result = await Suspend().HandleAsync(new SuspendOwnerCommand(AdminId, _owner.Id.Value, "fraude de CPF"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.SuspendedAt.Should().Be(Accounts.Now);
        _ownerAccounts.Verify(a => a.SuspendAsync(UserId, "fraude de CPF", AdminId, It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.OwnerSuspended && r.TargetId == _owner.Id.Value && r.Reason == "fraude de CPF"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Suspend_Should_LiftTheOwnerAgain_When_AccountCannotBeSuspended()
    {
        // Compensação: perfil e conta nunca ficam com status diferentes.
        _ownerAccounts
            .Setup(a => a.SuspendAsync(UserId, It.IsAny<string?>(), AdminId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(new Error("AUTH_ADMIN_SUSPENSION_FORBIDDEN", "no")));

        var result = await Suspend().HandleAsync(new SuspendOwnerCommand(AdminId, _owner.Id.Value, "x"), CancellationToken.None);

        result.FirstError!.Code.Should().Be("AUTH_ADMIN_SUSPENSION_FORBIDDEN");
        _owner.IsSuspended.Should().BeFalse();
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Suspend_Should_ReturnNotFound_When_OwnerDoesNotExist()
    {
        var result = await Suspend().HandleAsync(new SuspendOwnerCommand(AdminId, Guid.CreateVersion7(), "x"), CancellationToken.None);

        result.FirstError!.Code.Should().Be("OWNER_NOT_FOUND");
    }

    [Fact]
    public async Task Lift_Should_LiftOwnerAndAccount_When_Suspended()
    {
        _owner.Suspend(Accounts.Now);

        var result = await Lift().HandleAsync(new LiftOwnerSuspensionCommand(AdminId, _owner.Id.Value), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _owner.IsSuspended.Should().BeFalse();
        _ownerAccounts.Verify(a => a.LiftSuspensionAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.OwnerSuspensionLifted), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Lift_Should_RefuseWithoutTouchingTheAccount_When_CpfWasReleased()
    {
        _owner.Suspend(Accounts.Now);
        _owner.ReleaseCpf(Accounts.Now);

        var result = await Lift().HandleAsync(new LiftOwnerSuspensionCommand(AdminId, _owner.Id.Value), CancellationToken.None);

        result.FirstError!.Code.Should().Be("OWNER_CPF_RELEASED");
        _ownerAccounts.Verify(a => a.LiftSuspensionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ReleaseCpf_Should_ClearTheCpfAndRecordItMasked_When_OwnerIsSuspended()
    {
        _owner.Suspend(Accounts.Now);

        var result = await Release().HandleAsync(new ReleaseOwnerCpfCommand(AdminId, _owner.Id.Value, "documento conferido"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.Cpf.Should().BeNull();
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.OwnerCpfReleased
                && r.Reason == "documento conferido"
                && r.Details!["cpf"] == "***.***.247-25"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReleaseCpf_Should_Fail_When_OwnerIsNotSuspended()
    {
        var result = await Release().HandleAsync(new ReleaseOwnerCpfCommand(AdminId, _owner.Id.Value, "x"), CancellationToken.None);

        result.FirstError!.Code.Should().Be("OWNER_SUSPENSION_REQUIRED");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Validators_Should_RequireAReason_When_Missing(string? reason)
    {
        new SuspendOwnerCommandValidator().Validate(new SuspendOwnerCommand(AdminId, _owner.Id.Value, reason))
            .Errors.Should().ContainSingle(e => e.PropertyName == "Reason");
        new ReleaseOwnerCpfCommandValidator().Validate(new ReleaseOwnerCpfCommand(AdminId, _owner.Id.Value, reason))
            .Errors.Should().ContainSingle(e => e.PropertyName == "Reason");
    }

    private SuspendOwnerCommandHandler Suspend() =>
        new(_ownerRepository.Object, _unitOfWork.Object, _ownerAccounts.Object, _auditTrail.Object, _time);

    private LiftOwnerSuspensionCommandHandler Lift() =>
        new(_ownerRepository.Object, _unitOfWork.Object, _ownerAccounts.Object, _auditTrail.Object, _time);

    private ReleaseOwnerCpfCommandHandler Release() =>
        new(_ownerRepository.Object, _unitOfWork.Object, _ownerAccounts.Object, _auditTrail.Object, _time);
}
