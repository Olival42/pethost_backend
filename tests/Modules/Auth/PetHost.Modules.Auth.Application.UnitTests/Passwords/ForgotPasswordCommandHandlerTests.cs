using FluentAssertions;
using Moq;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Application.Passwords.ForgotPassword;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.TestKit;
using PetHost.Shared.Contracts.Audit;
using Xunit;

namespace PetHost.Modules.Auth.Application.UnitTests.Passwords;

public sealed class ForgotPasswordCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IPasswordResetTokenStore> _tokenStore = new();
    private readonly Mock<IPasswordResetNotifier> _notifier = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly ForgotPasswordCommandHandler _sut;

    public ForgotPasswordCommandHandlerTests()
    {
        _tokenStore
            .Setup(s => s.IssueAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PasswordResetToken("token-de-reset", Now.AddMinutes(30)));

        _sut = new ForgotPasswordCommandHandler(_userRepository.Object, _tokenStore.Object, _notifier.Object, _auditTrail.Object);
    }

    [Fact]
    public async Task HandleAsync_Should_IssueTokenAndNotify_When_AccountExists()
    {
        var user = new UserBuilder().WithId(TestIds.Of(42)).WithEmail("camila@exemplo.com").Build();
        _userRepository
            .Setup(r => r.GetByEmailAndRoleAsync(It.IsAny<Email>(), UserRole.Owner, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await _sut.HandleAsync(
            new ForgotPasswordCommand("Camila@Exemplo.com", "owner"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _tokenStore.Verify(s => s.IssueAsync(new UserId(TestIds.Of(42)), It.IsAny<CancellationToken>()), Times.Once);
        _notifier.Verify(
            n => n.NotifyAsync(
                It.Is<PasswordResetNotification>(p =>
                    p.Email == "camila@exemplo.com"
                    && p.FullName == "Camila Souza"
                    && p.Token == "token-de-reset"
                    && p.ExpiresAt == Now.AddMinutes(30)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_SucceedWithoutNotifying_When_AccountDoesNotExist()
    {
        // Mesma resposta da conta existente: o endpoint não pode revelar quem tem cadastro.
        _userRepository
            .Setup(r => r.GetByEmailAndRoleAsync(It.IsAny<Email>(), It.IsAny<UserRole>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await _sut.HandleAsync(
            new ForgotPasswordCommand("ninguem@exemplo.com", "owner"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _tokenStore.Verify(s => s.IssueAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifier.Verify(n => n.NotifyAsync(It.IsAny<PasswordResetNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_SucceedWithoutLookup_When_EmailIsMalformed()
    {
        var result = await _sut.HandleAsync(
            new ForgotPasswordCommand("nao-e-email", "owner"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _userRepository.VerifyNoOtherCalls();
        _notifier.VerifyNoOtherCalls();
    }
}
