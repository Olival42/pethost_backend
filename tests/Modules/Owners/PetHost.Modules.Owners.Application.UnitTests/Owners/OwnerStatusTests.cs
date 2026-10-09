using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Application.Owners.DeactivateMyOwner;
using PetHost.Modules.Owners.Application.Owners.ReactivateOwner;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;
using PetHost.Shared.Contracts.Audit;
using Xunit;

namespace PetHost.Modules.Owners.Application.UnitTests.Owners;

public sealed class DeactivateMyOwnerCommandHandlerTests
{
    private static readonly Guid Id = Guid.CreateVersion7();

    private readonly Mock<IOwnerRepository> _ownerRepository = new();
    private readonly Mock<IOwnersUnitOfWork> _unitOfWork = new();
    private readonly Mock<IOwnerAccounts> _ownerAccounts = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Owner _owner = Owner.Create(Id, Cpf.Create("52998224725").Value!, Accounts.Now);
    private readonly DeactivateMyOwnerCommandHandler _sut;

    public DeactivateMyOwnerCommandHandlerTests()
    {
        _ownerRepository.Setup(r => r.GetByUserIdAsync(Id, It.IsAny<CancellationToken>())).ReturnsAsync(_owner);
        _ownerAccounts.Setup(a => a.DeactivateAsync(Id, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());

        _sut = new DeactivateMyOwnerCommandHandler(
            _ownerRepository.Object, _unitOfWork.Object, _ownerAccounts.Object, _auditTrail.Object, new FakeTimeProvider(Accounts.Now));
    }

    [Fact]
    public async Task HandleAsync_Should_DeactivateOwnerAndAccount_When_Called()
    {
        var result = await _sut.HandleAsync(new DeactivateMyOwnerCommand(Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _owner.IsActive.Should().BeFalse();
        _ownerAccounts.Verify(a => a.DeactivateAsync(Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReactivateTheOwner_When_AccountCannotBeDeactivated()
    {
        // Compensação: perfil e conta nunca ficam com status diferentes.
        _ownerAccounts
            .Setup(a => a.DeactivateAsync(Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(new Error("AUTH_USER_NOT_FOUND", "gone")));

        var result = await _sut.HandleAsync(new DeactivateMyOwnerCommand(Id), CancellationToken.None);

        result.FirstError!.Code.Should().Be("AUTH_USER_NOT_FOUND");
        _owner.IsActive.Should().BeTrue();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_ProfileDoesNotExist()
    {
        var result = await _sut.HandleAsync(new DeactivateMyOwnerCommand(Guid.CreateVersion7()), CancellationToken.None);

        result.FirstError!.Code.Should().Be("OWNER_NOT_FOUND");
        _ownerAccounts.Verify(a => a.DeactivateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

public sealed class ReactivateOwnerCommandHandlerTests
{
    private static readonly Guid Id = Guid.CreateVersion7();

    private readonly Mock<IOwnerRepository> _ownerRepository = new();
    private readonly Mock<IOwnersUnitOfWork> _unitOfWork = new();
    private readonly Mock<IOwnerAccounts> _ownerAccounts = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Owner _owner = Owner.Create(Id, Cpf.Create("52998224725").Value!, Accounts.Now);
    private readonly ReactivateOwnerCommandHandler _sut;

    public ReactivateOwnerCommandHandlerTests()
    {
        _owner.Deactivate(Accounts.Now);
        _ownerRepository.Setup(r => r.GetByUserIdAsync(Id, It.IsAny<CancellationToken>())).ReturnsAsync(_owner);
        _ownerAccounts
            .Setup(a => a.ReactivateAsync("camila@exemplo.com", "Tutora@123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AccountSession>.Success(
                new AccountSession("jwt", "refresh", 1_791_461_700, Accounts.Summary(Id, "Camila"))));

        _sut = new ReactivateOwnerCommandHandler(
            _ownerRepository.Object, _unitOfWork.Object, _ownerAccounts.Object, _auditTrail.Object, new FakeTimeProvider(Accounts.Now));
    }

    [Fact]
    public async Task HandleAsync_Should_ReactivateAccountThenOwner_When_CredentialsAreRight()
    {
        var result = await _sut.HandleAsync(new ReactivateOwnerCommand("camila@exemplo.com", "Tutora@123"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("jwt");
        result.Value.Owner.IsActive.Should().BeTrue();
        _owner.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_Should_NotTouchTheOwner_When_CredentialsAreWrong()
    {
        _ownerAccounts
            .Setup(a => a.ReactivateAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AccountSession>.Failure(new Error("AUTH_INVALID_CREDENTIALS", "wrong")));

        var result = await _sut.HandleAsync(new ReactivateOwnerCommand("camila@exemplo.com", "Errada@123"), CancellationToken.None);

        result.FirstError!.Code.Should().Be("AUTH_INVALID_CREDENTIALS");
        _owner.IsActive.Should().BeFalse();
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_DeactivateTheAccountAgain_When_SavingTheOwnerFails()
    {
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("banco fora do ar"));

        var act = () => _sut.HandleAsync(new ReactivateOwnerCommand("camila@exemplo.com", "Tutora@123"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _ownerAccounts.Verify(a => a.DeactivateAsync(Id, CancellationToken.None), Times.Once);
    }
}
