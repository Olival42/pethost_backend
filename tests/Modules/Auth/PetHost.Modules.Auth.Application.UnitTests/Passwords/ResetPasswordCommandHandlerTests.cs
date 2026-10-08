using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Application.Passwords.ResetPassword;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Auth.Application.UnitTests.Passwords;

public sealed class ResetPasswordCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private const string NewHash =
        "$argon2id$v=19$m=19456,t=2,p=1$bm92b3NhbHRub3Zvc2FsdA$bm92b2hhc2hub3ZvaGFzaG5vdm9oYXNobm92b2hhc2g";

    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IAuthUnitOfWork> _unitOfWork = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<IPasswordResetTokenStore> _tokenStore = new();
    private readonly Mock<IRefreshTokenStore> _refreshTokenStore = new();
    private readonly ResetPasswordCommandHandler _sut;

    public ResetPasswordCommandHandlerTests()
    {
        _passwordHasher.Setup(h => h.Hash(It.IsAny<string>())).Returns(NewHash);

        _sut = new ResetPasswordCommandHandler(
            _userRepository.Object,
            _unitOfWork.Object,
            _passwordHasher.Object,
            _tokenStore.Object,
            _refreshTokenStore.Object,
            new FakeTimeProvider(Now));
    }

    [Fact]
    public async Task HandleAsync_Should_ChangePasswordAndRevokeSessions_When_TokenIsValid()
    {
        var user = new UserBuilder().WithId(TestIds.Of(42)).Build();
        GivenTokenBelongsTo(42);
        GivenUser(user);

        var result = await _sut.HandleAsync(
            new ResetPasswordCommand("token-valido", "Nova@Senha123"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        user.PasswordHash.Value.Should().Be(NewHash);
        user.UpdatedAt.Should().Be(Now);
        _passwordHasher.Verify(h => h.Hash("Nova@Senha123"), Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _refreshTokenStore.Verify(
            s => s.RevokeAllAsync(new UserId(TestIds.Of(42)), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnTokenInvalid_When_TokenIsUnknownOrUsed()
    {
        _tokenStore
            .Setup(s => s.ConsumeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserId?)null);

        var result = await _sut.HandleAsync(
            new ResetPasswordCommand("token-usado", "Nova@Senha123"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError!.Code.Should().Be("AUTH_PASSWORD_RESET_TOKEN_INVALID");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _refreshTokenStore.Verify(
            s => s.RevokeAllAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnTokenInvalid_When_AccountNoLongerExists()
    {
        GivenTokenBelongsTo(42);
        _userRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await _sut.HandleAsync(
            new ResetPasswordCommand("token-valido", "Nova@Senha123"),
            CancellationToken.None);

        result.FirstError!.Code.Should().Be("AUTH_PASSWORD_RESET_TOKEN_INVALID");
        _passwordHasher.Verify(h => h.Hash(It.IsAny<string>()), Times.Never);
    }

    private void GivenTokenBelongsTo(int userNumber) =>
        _tokenStore
            .Setup(s => s.ConsumeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserId(TestIds.Of(userNumber)));

    private void GivenUser(User user) =>
        _userRepository
            .Setup(r => r.GetByIdAsync(user.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
}
