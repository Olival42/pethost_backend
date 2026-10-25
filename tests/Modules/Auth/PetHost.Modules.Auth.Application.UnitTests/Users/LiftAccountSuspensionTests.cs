using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Application.Users.SuspendAccount;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Contracts.Audit;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Auth.Application.UnitTests.Users;

public sealed class LiftAccountSuspensionCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 15, 0, 0, TimeSpan.Zero);

    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IAuthUnitOfWork> _unitOfWork = new();
    private readonly Mock<IAccountSuspensionLiftedNotifier> _notifier = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly LiftAccountSuspensionCommandHandler _sut;

    public LiftAccountSuspensionCommandHandlerTests()
    {
        _sut = new LiftAccountSuspensionCommandHandler(
            _userRepository.Object,
            _unitOfWork.Object,
            _notifier.Object,
            _auditTrail.Object,
            new FakeTimeProvider(Now));
    }

    [Fact]
    public async Task HandleAsync_Should_NotifyThePerson_When_TheSuspensionIsLifted()
    {
        // Arrange
        var user = Given(new UserBuilder().WithId(TestIds.Of(7)).WithRole(UserRole.Host).Build());
        user.Suspend("Fraude", TestIds.Of(99), Now.AddDays(-1));

        // Act
        var result = await _sut.HandleAsync(new LiftAccountSuspensionCommand(user.Id.Value), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _notifier.Verify(n => n.NotifyAsync(
            It.Is<AccountSuspensionLiftedNotification>(x =>
                x.Email == user.Email.Value && x.Role == UserRole.Host && x.IsActive && x.LiftedAt == Now),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_TellTheAccountIsStillInactive_When_ItWasInactiveBeforeTheSuspension()
    {
        // Arrange
        var user = Given(new UserBuilder().WithId(TestIds.Of(7)).Build());
        user.Deactivate(Now.AddDays(-2));
        user.Suspend("Fraude", TestIds.Of(99), Now.AddDays(-1));

        // Act
        await _sut.HandleAsync(new LiftAccountSuspensionCommand(user.Id.Value), CancellationToken.None);

        // Assert
        _notifier.Verify(n => n.NotifyAsync(
            It.Is<AccountSuspensionLiftedNotification>(x => !x.IsActive),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_NotNotify_When_TheAccountIsNotSuspended()
    {
        // Arrange
        var user = Given(new UserBuilder().WithId(TestIds.Of(7)).Build());

        // Act
        var result = await _sut.HandleAsync(new LiftAccountSuspensionCommand(user.Id.Value), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _notifier.Verify(n => n.NotifyAsync(It.IsAny<AccountSuspensionLiftedNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFoundWithoutSaving_When_TheAccountDoesNotExist()
    {
        // Act
        var result = await _sut.HandleAsync(new LiftAccountSuspensionCommand(TestIds.Of(7)), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("AUTH_USER_NOT_FOUND");
        _unitOfWork.VerifyNoOtherCalls();
        _notifier.VerifyNoOtherCalls();
    }

    private User Given(User user)
    {
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        return user;
    }
}
