using FluentAssertions;
using Moq;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Application.Sessions.CreateSession;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Auth.Application.UnitTests.Sessions;

public sealed class CreateSessionCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<IAccessTokenGenerator> _accessTokenGenerator = new();
    private readonly Mock<IRefreshTokenStore> _refreshTokenStore = new();
    private readonly CreateSessionCommandHandler _sut;

    public CreateSessionCommandHandlerTests()
    {
        _sut = new CreateSessionCommandHandler(
            _userRepository.Object,
            _passwordHasher.Object,
            _accessTokenGenerator.Object,
            _refreshTokenStore.Object);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnSessionWithUser_When_CredentialsAreValid()
    {
        var user = new UserBuilder()
            .WithId(TestIds.Of(42))
            .WithEmail("camila@exemplo.com")
            .WithFullName("Camila Souza")
            .WithRole(UserRole.Owner)
            .Build();

        GivenUser(user);
        GivenPasswordMatches();
        GivenAccessToken("jwt-token", Now.AddMinutes(15), 900);
        GivenRefreshToken("refresh-token", Now.AddDays(30));

        var result = await _sut.HandleAsync(
            new CreateSessionCommand("camila@exemplo.com", "senha-correta", "owner"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var session = result.Value!;
        session.AccessToken.Should().Be("jwt-token");
        session.RefreshToken.Should().Be("refresh-token");
        session.ExpiresAt.Should().Be(Now.AddMinutes(15).ToUnixTimeSeconds());

        session.User.Id.Should().Be(TestIds.Of(42));
        session.User.FullName.Should().Be("Camila Souza");
        session.User.Email.Should().Be("camila@exemplo.com");
        session.User.Role.Should().Be("owner");
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnInvalidCredentials_When_UserDoesNotExist()
    {
        GivenNoUser();

        var result = await _sut.HandleAsync(
            new CreateSessionCommand("ninguem@exemplo.com", "qualquer-senha", "owner"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError!.Code.Should().Be("AUTH_INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task HandleAsync_Should_StillComputeAHash_When_UserDoesNotExist()
    {
        // Sem isto, responder "conta inexistente" sairia bem mais rapido do que
        // "senha errada", e a duracao da resposta viraria oraculo de contas.
        GivenNoUser();

        await _sut.HandleAsync(
            new CreateSessionCommand("ninguem@exemplo.com", "qualquer-senha", "owner"),
            CancellationToken.None);

        _passwordHasher.Verify(h => h.Hash("qualquer-senha"), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnInvalidCredentials_When_PasswordIsWrong()
    {
        GivenUser(new UserBuilder().WithId(TestIds.Of(1)).Build());
        GivenPasswordDoesNotMatch();

        var result = await _sut.HandleAsync(
            new CreateSessionCommand("camila@exemplo.com", "senha-errada", "owner"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError!.Code.Should().Be("AUTH_INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task HandleAsync_Should_NotIssueRefreshToken_When_PasswordIsWrong()
    {
        GivenUser(new UserBuilder().WithId(TestIds.Of(1)).Build());
        GivenPasswordDoesNotMatch();

        await _sut.HandleAsync(
            new CreateSessionCommand("camila@exemplo.com", "senha-errada", "owner"),
            CancellationToken.None);

        _refreshTokenStore.Verify(
            s => s.IssueAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnInvalidCredentials_When_EmailIsMalformed()
    {
        var result = await _sut.HandleAsync(
            new CreateSessionCommand("nao-e-um-email", "senha", "owner"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();

        // Formato invalido devolve o mesmo erro generico: nada de responder
        // "esse e-mail nem existe".
        result.FirstError!.Code.Should().Be("AUTH_INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnRoleUnknown_When_RoleIsNotRecognized()
    {
        var result = await _sut.HandleAsync(
            new CreateSessionCommand("camila@exemplo.com", "senha", "tutor"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError!.Field.Should().Be("role");
    }

    [Fact]
    public async Task HandleAsync_Should_LookUpUserByEmailAndRole_When_SameEmailHasTwoAccounts()
    {
        // A Dona Cida pode ter conta de tutora e de anfitria com o mesmo e-mail:
        // a busca precisa levar o papel, senao acha a conta errada.
        var host = new UserBuilder().WithId(TestIds.Of(7)).WithEmail("cida@exemplo.com").WithRole(UserRole.Host).Build();

        GivenUser(host);
        GivenPasswordMatches();
        GivenAccessToken("jwt", Now.AddMinutes(15), 900);
        GivenRefreshToken("refresh", Now.AddDays(30));

        await _sut.HandleAsync(
            new CreateSessionCommand("cida@exemplo.com", "senha", "host"),
            CancellationToken.None);

        _userRepository.Verify(
            r => r.GetByEmailAndRoleAsync(
                It.Is<Email>(e => e.Value == "cida@exemplo.com"),
                UserRole.Host,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_AllowLogin_When_RoleIsAdmin()
    {
        // O admin criado pelo seed entra pela mesma rota.
        var admin = new UserBuilder().WithId(TestIds.Of(1)).WithRole(UserRole.Admin).Build();

        GivenUser(admin);
        GivenPasswordMatches();
        GivenAccessToken("jwt", Now.AddMinutes(15), 900);
        GivenRefreshToken("refresh", Now.AddDays(30));

        var result = await _sut.HandleAsync(
            new CreateSessionCommand("admin@pethost.com", "senha", "admin"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.User.Role.Should().Be("admin");
    }

    [Fact]
    public async Task HandleAsync_Should_NormalizeEmail_When_CasingDiffers()
    {
        GivenUser(new UserBuilder().WithId(TestIds.Of(1)).Build());
        GivenPasswordMatches();
        GivenAccessToken("jwt", Now.AddMinutes(15), 900);
        GivenRefreshToken("refresh", Now.AddDays(30));

        await _sut.HandleAsync(
            new CreateSessionCommand("  CAMILA@EXEMPLO.COM  ", "senha", "owner"),
            CancellationToken.None);

        _userRepository.Verify(
            r => r.GetByEmailAndRoleAsync(
                It.Is<Email>(e => e.Value == "camila@exemplo.com"),
                It.IsAny<UserRole>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private void GivenUser(User user) =>
        _userRepository
            .Setup(r => r.GetByEmailAndRoleAsync(
                It.IsAny<Email>(), It.IsAny<UserRole>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

    private void GivenNoUser() =>
        _userRepository
            .Setup(r => r.GetByEmailAndRoleAsync(
                It.IsAny<Email>(), It.IsAny<UserRole>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

    private void GivenPasswordMatches() =>
        _passwordHasher
            .Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(true);

    private void GivenPasswordDoesNotMatch() =>
        _passwordHasher
            .Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(false);

    private void GivenAccessToken(string value, DateTimeOffset expiresAt, long expiresIn) =>
        _accessTokenGenerator
            .Setup(g => g.Generate(It.IsAny<User>()))
            .Returns(new AccessToken(value, expiresAt, expiresIn));

    private void GivenRefreshToken(string value, DateTimeOffset expiresAt) =>
        _refreshTokenStore
            .Setup(s => s.IssueAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshToken(value, expiresAt));
}
