using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Application.Hosts.DeleteMyHost;
using PetHost.Modules.Hosts.Domain.Errors;
using PetHost.Modules.Hosts.Domain.HostApplications;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Shared.Contracts.Audit;
using PetHost.Shared.Contracts.Storage;
using PetHost.Shared.Kernel.Errors;
using PetHost.Shared.Kernel.Results;
using Xunit;
using static PetHost.Modules.Hosts.Application.UnitTests.HostApplications.HostsTestData;

namespace PetHost.Modules.Hosts.Application.UnitTests.Hosts;

/// <summary>Exclusão da conta de anfitrião a pedido do titular (LGPD).</summary>
public sealed class DeleteMyHostTests
{
    private const string Password = "Anfitria@123";
    private const string Avatar = "https://img.pethost.test/avatars/cida.png";

    private readonly Mock<IHostRepository> _hosts = new();
    private readonly Mock<IHostApplicationRepository> _applications = new();
    private readonly Mock<IHostsUnitOfWork> _unitOfWork = UnitOfWork();
    private readonly Mock<IHostAccounts> _accounts = Accounts();
    private readonly Mock<IHostBookings> _bookings = new();
    private readonly Mock<IHostDataErasure> _dataErasure = new();
    private readonly Mock<IImageStorage> _images = new();
    private readonly Mock<IPrivateImageStorage> _privateImages = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly Host _host = Host.CreateIndividual(UserId, AnyCpf, Guid.CreateVersion7(), AdminId, Now);
    private readonly HostApplication _approved = Submitted();
    private readonly List<string> _calls = [];

    public DeleteMyHostTests()
    {
        _hosts.Setup(h => h.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync(_host);
        _applications.Setup(a => a.ListByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync([_approved]);
        _accounts.Setup(a => a.VerifyPasswordAsync(UserId, Password, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _accounts
            .Setup(a => a.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Account(UserId) with { AvatarUrl = Avatar }]);
        _accounts
            .Setup(a => a.EraseAsync(UserId, It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("account"))
            .ReturnsAsync(Result.Success());
        _dataErasure
            .Setup(e => e.EraseAsync(UserId, It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("modules"))
            .Returns(Task.CompletedTask);
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("profile"))
            .ReturnsAsync(Result.Success());
        _privateImages
            .Setup(s => s.DeleteAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("document"))
            .Returns(Task.CompletedTask);
        _images
            .Setup(s => s.DeleteAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("avatar"))
            .Returns(Task.CompletedTask);
    }

    private DeleteMyHostCommandHandler Handler() => new(
        _hosts.Object,
        _applications.Object,
        _unitOfWork.Object,
        _accounts.Object,
        _bookings.Object,
        _dataErasure.Object,
        _images.Object,
        _privateImages.Object,
        _auditTrail.Object,
        new FakeTimeProvider(Now.AddDays(1)));

    [Fact]
    public async Task HandleAsync_Should_EraseEverythingInOrder_When_PasswordMatches()
    {
        // Act
        var result = await Handler().HandleAsync(new DeleteMyHostCommand(UserId, Password), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _host.IsErased.Should().BeTrue();
        _host.Cpf.Should().BeNull();
        _calls.Should().Equal(
            ["modules", "profile", "account", "document", "document", "document", "avatar"],
            "a conta vai por último; os arquivos só saem depois do banco");
        _dataErasure.Verify(e => e.EraseAsync(UserId, _host.Id.Value, It.IsAny<CancellationToken>()), Times.Once);
        _applications.Verify(a => a.Remove(_approved), Times.Once);
        _privateImages.Verify(s => s.DeleteAsync("host-applications/selfie.jpg", It.IsAny<CancellationToken>()), Times.Once);
        _images.Verify(s => s.DeleteAsync(Avatar, It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(
            t => t.RecordAsync(It.Is<AuditRecord>(r => r.Action == AuditActions.HostErased && r.TargetId == _host.Id.Value), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_EraseTheApplicationAndTheAccount_When_HostIsNotApprovedYet()
    {
        // Arrange
        _hosts.Setup(h => h.GetByUserIdAsync(UserId, It.IsAny<CancellationToken>())).ReturnsAsync((Host?)null);

        // Act
        var result = await Handler().HandleAsync(new DeleteMyHostCommand(UserId, Password), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _dataErasure.Verify(e => e.EraseAsync(UserId, null, It.IsAny<CancellationToken>()), Times.Once);
        _applications.Verify(a => a.Remove(_approved), Times.Once);
        _accounts.Verify(a => a.EraseAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
        _bookings.Verify(b => b.HasOpenBookingAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _auditTrail.Verify(t => t.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_RefuseWithoutTouchingAnything_When_PasswordIsWrong()
    {
        // Act
        var result = await Handler().HandleAsync(new DeleteMyHostCommand(UserId, "errada"), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(HostsErrors.CurrentPasswordIncorrect);
        _calls.Should().BeEmpty();
        _host.IsErased.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_Should_RefuseWithoutTouchingAnything_When_HostHasAnOpenBooking()
    {
        // Arrange
        _bookings.Setup(b => b.HasOpenBookingAsync(_host.Id.Value, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        // Act
        var result = await Handler().HandleAsync(new DeleteMyHostCommand(UserId, Password), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(HostsErrors.HasOpenBooking);
        _calls.Should().BeEmpty();
        _host.IsErased.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_Should_KeepTheFiles_When_TheAccountCannotBeErased()
    {
        // Arrange
        _accounts
            .Setup(a => a.EraseAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(new Error("AUTH_USER_NOT_FOUND", "gone")));

        // Act
        var result = await Handler().HandleAsync(new DeleteMyHostCommand(UserId, Password), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("AUTH_USER_NOT_FOUND");
        _privateImages.Verify(s => s.DeleteAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _images.Verify(s => s.DeleteAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_StopBeforeTheAccount_When_TheProfileCannotBeSaved()
    {
        // Arrange
        _unitOfWork
            .Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(new Error("HOST_CONFLICT", "changed")));

        // Act
        var result = await Handler().HandleAsync(new DeleteMyHostCommand(UserId, Password), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("HOST_CONFLICT");
        _accounts.Verify(a => a.EraseAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
