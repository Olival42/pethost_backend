using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Modules.Owners.Application.Owners.DeleteMyOwner;
using PetHost.Modules.Owners.Domain.Errors;
using PetHost.Modules.Owners.Domain.Owners;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;
using Xunit;

namespace PetHost.Modules.Owners.Application.UnitTests.Owners;

public sealed class DeleteMyOwnerCommandHandlerTests
{
    private const string Password = "Tutora@123";
    private const string Avatar = "https://img.pethost.test/avatars/camila.png";

    private static readonly Guid Id = Guid.CreateVersion7();

    private readonly Mock<IOwnerRepository> _ownerRepository = new();
    private readonly Mock<IOwnersUnitOfWork> _unitOfWork = new();
    private readonly Mock<IOwnerAccounts> _ownerAccounts = new();
    private readonly Mock<IOwnerBookings> _ownerBookings = new();
    private readonly Mock<IOwnerDataErasure> _dataErasure = new();
    private readonly Mock<IImageStorage> _imageStorage = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Owner _owner = Owner.Create(Id, Cpf.Create("52998224725").Value!, Accounts.Now);
    private readonly List<string> _calls = [];
    private readonly DeleteMyOwnerCommandHandler _sut;

    public DeleteMyOwnerCommandHandlerTests()
    {
        _ownerRepository.Setup(r => r.GetByUserIdAsync(Id, It.IsAny<CancellationToken>())).ReturnsAsync(_owner);
        _ownerAccounts.Setup(a => a.VerifyPasswordAsync(Id, Password, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _ownerAccounts
            .Setup(a => a.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Accounts.Summary(Id, "Camila") with { AvatarUrl = Avatar }]);
        _ownerAccounts
            .Setup(a => a.EraseAsync(Id, It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("account"))
            .ReturnsAsync(Result.Success());
        _dataErasure
            .Setup(e => e.EraseAsync(Id, _owner.Id.Value, It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("modules"))
            .Returns(Task.CompletedTask);
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("profile"))
            .ReturnsAsync(1);
        _imageStorage
            .Setup(s => s.DeleteAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("avatar"))
            .Returns(Task.CompletedTask);

        _sut = new DeleteMyOwnerCommandHandler(
            _ownerRepository.Object,
            _unitOfWork.Object,
            _ownerAccounts.Object,
            _ownerBookings.Object,
            _dataErasure.Object,
            _imageStorage.Object,
            _auditTrail.Object,
            new FakeTimeProvider(Accounts.Now));
    }

    [Fact]
    public async Task HandleAsync_Should_EraseEverythingInOrder_When_PasswordMatches()
    {
        var result = await _sut.HandleAsync(new DeleteMyOwnerCommand(Id, Password), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _owner.IsErased.Should().BeTrue();
        _owner.Cpf.Should().BeNull();
        _calls.Should().Equal(["modules", "profile", "account", "avatar"], "a conta vai por último, para a pessoa poder repetir se algo falhar");
        _imageStorage.Verify(s => s.DeleteAsync(Avatar, It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(
            t => t.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.OwnerErased && r.TargetId == _owner.Id.Value), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_RefuseWithoutTouchingAnything_When_PasswordIsWrong()
    {
        var result = await _sut.HandleAsync(new DeleteMyOwnerCommand(Id, "errada"), CancellationToken.None);

        result.FirstError.Should().Be(OwnersErrors.CurrentPasswordIncorrect);
        _calls.Should().BeEmpty();
        _owner.IsErased.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_Should_RefuseWithoutTouchingAnything_When_OwnerHasAnOpenBooking()
    {
        _ownerBookings.Setup(b => b.HasOpenBookingAsync(_owner.Id.Value, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _sut.HandleAsync(new DeleteMyOwnerCommand(Id, Password), CancellationToken.None);

        result.FirstError.Should().Be(OwnersErrors.HasOpenBooking);
        _calls.Should().BeEmpty();
        _owner.IsErased.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFound_When_ProfileDoesNotExist()
    {
        var result = await _sut.HandleAsync(new DeleteMyOwnerCommand(Guid.CreateVersion7(), Password), CancellationToken.None);

        result.FirstError.Should().Be(OwnersErrors.ProfileNotFound);
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_Should_KeepTheAvatar_When_TheAccountCannotBeErased()
    {
        _ownerAccounts
            .Setup(a => a.EraseAsync(Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(new Error("AUTH_USER_NOT_FOUND", "gone")));

        var result = await _sut.HandleAsync(new DeleteMyOwnerCommand(Id, Password), CancellationToken.None);

        result.FirstError!.Code.Should().Be("AUTH_USER_NOT_FOUND");
        _imageStorage.Verify(s => s.DeleteAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditTrail.Verify(t => t.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_FinishTheAccount_When_TheProfileWasErasedInAPreviousTry()
    {
        // Repetir depois de uma falha no meio: o perfil já está excluído, falta a conta.
        _owner.Erase(Accounts.Now);

        var result = await _sut.HandleAsync(new DeleteMyOwnerCommand(Id, Password), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _calls.Should().Equal("modules", "account", "avatar");
        _auditTrail.Verify(t => t.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

public sealed class DeleteMyOwnerCommandValidatorTests
{
    [Fact]
    public void Validate_Should_RequireThePassword_When_Missing()
    {
        var result = new DeleteMyOwnerCommandValidator().Validate(new DeleteMyOwnerCommand(Guid.CreateVersion7(), " "));

        result.Errors.Should().ContainSingle(e => e.PropertyName == "CurrentPassword");
    }
}
