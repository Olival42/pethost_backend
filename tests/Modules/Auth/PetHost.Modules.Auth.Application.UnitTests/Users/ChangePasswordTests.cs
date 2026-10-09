using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Application.Users.ChangePassword;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.TestKit;
using PetHost.Shared.Contracts.Audit;
using Xunit;

namespace PetHost.Modules.Auth.Application.UnitTests.Users;

public sealed class ChangePasswordCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private const string NewHash =
        "$argon2id$v=19$m=19456,t=2,p=1$bm92b3NhbHRub3Zvc2FsdA$bm92b2hhc2hub3ZvaGFzaG5vdm9oYXNobm92b2hhc2g";

    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IAuthUnitOfWork> _unitOfWork = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<IAccessTokenGenerator> _accessTokenGenerator = new();
    private readonly Mock<IRefreshTokenStore> _refreshTokenStore = new();
    private readonly Mock<IAccessTokenRevocationStore> _accessTokenRevocationStore = new();
    private readonly Mock<IPasswordChangedNotifier> _notifier = new();
    private readonly Mock<IAuditTrail> _auditTrail = new();
    private readonly User _user = new UserBuilder().WithId(TestIds.Of(7)).Build();
    private readonly ChangePasswordCommandHandler _sut;

    public ChangePasswordCommandHandlerTests()
    {
        _userRepository.Setup(r => r.GetByIdAsync(_user.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_user);
        _passwordHasher.Setup(h => h.Verify("Atual@123", UserBuilder.SampleHash)).Returns(true);
        _passwordHasher.Setup(h => h.Hash(It.IsAny<string>())).Returns(NewHash);
        _accessTokenGenerator
            .Setup(g => g.Generate(It.IsAny<User>()))
            .Returns(new AccessToken("novo-jwt", Now.AddMinutes(15), 900));
        _refreshTokenStore
            .Setup(s => s.IssueAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshToken("novo-refresh", Now.AddDays(30)));

        _sut = new ChangePasswordCommandHandler(
            _userRepository.Object,
            _unitOfWork.Object,
            _passwordHasher.Object,
            _accessTokenGenerator.Object,
            _refreshTokenStore.Object,
            _accessTokenRevocationStore.Object,
            _notifier.Object,
            _auditTrail.Object,
            new FakeTimeProvider(Now));
    }

    [Fact]
    public async Task HandleAsync_Should_ChangePasswordRevokeEverythingAndOpenNewSession_When_CurrentPasswordIsRight()
    {
        var sequence = new List<string>();
        _accessTokenRevocationStore
            .Setup(s => s.RevokeAllAsync(_user.Id, It.IsAny<CancellationToken>()))
            .Callback(() => sequence.Add("revoke"));
        _accessTokenGenerator
            .Setup(g => g.Generate(It.IsAny<User>()))
            .Callback(() => sequence.Add("issue"))
            .Returns(new AccessToken("novo-jwt", Now.AddMinutes(15), 900));

        var result = await _sut.HandleAsync(Command("Atual@123", "Nova@Senha123"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("novo-jwt");
        result.Value.RefreshToken.Should().Be("novo-refresh");
        _user.PasswordHash.Value.Should().Be(NewHash);
        _refreshTokenStore.Verify(s => s.RevokeAllAsync(_user.Id, It.IsAny<CancellationToken>()), Times.Once);
        sequence.Should().Equal(["revoke", "issue"], "a sessão nova é emitida depois da revogação, senão cairia junto");
        _notifier.Verify(
            n => n.NotifyAsync(It.Is<PasswordChangedNotification>(x => x.Email == _user.Email.Value && x.ChangedAt == Now), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnFieldError_When_CurrentPasswordIsWrong()
    {
        var result = await _sut.HandleAsync(Command("Errada@123", "Nova@Senha123"), CancellationToken.None);

        result.FirstError!.Field.Should().Be("currentPassword");
        result.FirstError.Code.Should().Be("VALIDATION_ERROR", "400 no campo, e não 401: a sessão continua válida");
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _refreshTokenStore.Verify(s => s.RevokeAllAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>()), Times.Never);
        _notifier.Verify(n => n.NotifyAsync(It.IsAny<PasswordChangedNotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnDeactivated_When_AccountIsInactive()
    {
        _user.Deactivate(Now);

        var result = await _sut.HandleAsync(Command("Atual@123", "Nova@Senha123"), CancellationToken.None);

        result.FirstError!.Code.Should().Be("AUTH_ACCOUNT_DEACTIVATED");
    }

    private ChangePasswordCommand Command(string current, string next) => new(_user.Id.Value, current, next);
}

public sealed class ChangePasswordCommandValidatorTests
{
    private readonly ChangePasswordCommandValidator _sut = new();

    [Fact]
    public void Validate_Should_ReportBothFields_When_CurrentIsMissingAndNewIsWeak()
    {
        var result = _sut.Validate(new ChangePasswordCommand(Guid.CreateVersion7(), "", "fraca"));

        result.Errors.Select(e => e.PropertyName).Distinct().Should().BeEquivalentTo(["CurrentPassword", "NewPassword"]);
    }

    [Fact]
    public void Validate_Should_Fail_When_NewPasswordEqualsCurrent()
    {
        var result = _sut.Validate(new ChangePasswordCommand(Guid.CreateVersion7(), "Atual@123", "Atual@123"));

        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "New password must be different from the current password.");
    }

    [Fact]
    public void Validate_Should_Pass_When_PasswordsAreValid()
    {
        _sut.Validate(new ChangePasswordCommand(Guid.CreateVersion7(), "Atual@123", "Nova@Senha123")).IsValid.Should().BeTrue();
    }
}
