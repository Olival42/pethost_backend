using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Application.Users.SuspendAccount;
using PetHost.Modules.Auth.Domain.Errors;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Contracts.Audit;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Auth.Application.UnitTests.Users;

public sealed class SuspendAccountCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid AdminId = TestIds.Of(99);

    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IAuthUnitOfWork> _unitOfWork = new();
    private readonly Mock<IRefreshTokenStore> _refreshTokens = new();
    private readonly Mock<IAccessTokenRevocationStore> _accessTokens = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly SuspendAccountCommandHandler _sut;

    public SuspendAccountCommandHandlerTests()
    {
        _sut = new SuspendAccountCommandHandler(
            _userRepository.Object,
            _unitOfWork.Object,
            _refreshTokens.Object,
            _accessTokens.Object,
            _auditTrail.Object,
            new FakeTimeProvider(Now));
    }

    [Fact]
    public async Task HandleAsync_Should_SuspendEndEverySessionAndRecord_When_TheAccountIsActive()
    {
        // Arrange
        var user = Given(new UserBuilder().WithId(TestIds.Of(7)).Build());

        // Act
        var result = await _sut.HandleAsync(new SuspendAccountCommand(user.Id.Value, "Uso de CPF de terceiro", AdminId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        user.IsSuspended.Should().BeTrue();
        user.SuspendedBy.Should().Be(AdminId);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _refreshTokens.Verify(s => s.RevokeAllAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _accessTokens.Verify(s => s.RevokeAllAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(
            It.Is<AuditRecord>(r => r.Action == AuditActions.AccountSuspended && r.TargetId == user.Id.Value && r.Reason == "Uso de CPF de terceiro"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_EndTheSessionsWithoutRecordingAgain_When_TheAccountIsAlreadySuspended()
    {
        // Arrange
        var user = Given(new UserBuilder().WithId(TestIds.Of(7)).Build());
        user.Suspend("Primeiro motivo", AdminId, Now.AddDays(-1));

        // Act
        var result = await _sut.HandleAsync(new SuspendAccountCommand(user.Id.Value, "Outro motivo", AdminId), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        user.SuspensionReason.Should().Be("Primeiro motivo", "vale a primeira suspensão");
        _refreshTokens.Verify(s => s.RevokeAllAsync(user.Id, It.IsAny<CancellationToken>()), Times.Once);
        _auditTrail.Verify(a => a.RecordAsync(It.IsAny<AuditRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNotFoundWithoutRevoking_When_TheAccountDoesNotExist()
    {
        // Act
        var result = await _sut.HandleAsync(new SuspendAccountCommand(TestIds.Of(7), "Fraude", AdminId), CancellationToken.None);

        // Assert
        result.FirstError!.Code.Should().Be("AUTH_USER_NOT_FOUND");
        _refreshTokens.VerifyNoOtherCalls();
        _accessTokens.VerifyNoOtherCalls();
        _unitOfWork.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_Should_RefuseWithoutSaving_When_TheAccountIsAnAdmin()
    {
        // Arrange
        var admin = Given(new UserBuilder().WithId(TestIds.Of(7)).WithRole(UserRole.Admin).Build());

        // Act
        var result = await _sut.HandleAsync(new SuspendAccountCommand(admin.Id.Value, "Fraude", AdminId), CancellationToken.None);

        // Assert
        result.FirstError.Should().Be(AuthErrors.AdminSuspensionForbidden);
        admin.IsSuspended.Should().BeFalse();
        _unitOfWork.VerifyNoOtherCalls();
        _refreshTokens.VerifyNoOtherCalls();
        _auditTrail.VerifyNoOtherCalls();
    }

    private User Given(User user)
    {
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(user);
        return user;
    }
}
