using FluentAssertions;
using Moq;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Application.Sessions.RefreshSession;
using PetHost.Modules.Auth.Application.Sessions.RevokeSession;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Auth.Application.UnitTests.Sessions;

public sealed class RefreshSessionCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IAccessTokenGenerator> _accessTokenGenerator = new();
    private readonly Mock<IRefreshTokenStore> _refreshTokenStore = new();
    private readonly RefreshSessionCommandHandler _sut;

    public RefreshSessionCommandHandlerTests()
    {
        _accessTokenGenerator
            .Setup(g => g.Generate(It.IsAny<User>()))
            .Returns(new AccessToken("novo-jwt", Now.AddMinutes(15), 900));

        _sut = new RefreshSessionCommandHandler(
            _userRepository.Object,
            _accessTokenGenerator.Object,
            _refreshTokenStore.Object);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnNewTokenPair_When_RefreshTokenIsValid()
    {
        var user = new UserBuilder().WithId(TestIds.Of(42)).WithRole(UserRole.Owner).Build();
        GivenTokenBelongsTo(42);
        _userRepository
            .Setup(r => r.GetByIdAsync(new UserId(TestIds.Of(42)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        GivenNewRefreshToken("refresh-novo");

        var result = await _sut.HandleAsync(
            new RefreshSessionCommand("refresh-antigo"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value!.AccessToken.Should().Be("novo-jwt");
        result.Value.RefreshToken.Should().Be("refresh-novo");
        result.Value.User.Id.Should().Be(TestIds.Of(42));
    }

    [Fact]
    public async Task HandleAsync_Should_RotateToken_When_RefreshSucceeds()
    {
        // Uso unico: o token apresentado e consumido e um novo e emitido.
        var user = new UserBuilder().WithId(TestIds.Of(42)).Build();
        GivenTokenBelongsTo(42);
        _userRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        GivenNewRefreshToken("refresh-novo");

        var result = await _sut.HandleAsync(
            new RefreshSessionCommand("refresh-antigo"),
            CancellationToken.None);

        _refreshTokenStore.Verify(
            s => s.ConsumeAsync("refresh-antigo", It.IsAny<CancellationToken>()),
            Times.Once);
        _refreshTokenStore.Verify(
            s => s.IssueAsync(new UserId(TestIds.Of(42)), It.IsAny<CancellationToken>()),
            Times.Once);
        result.Value!.RefreshToken.Should().NotBe("refresh-antigo");
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnInvalid_When_TokenIsUnknownOrAlreadyUsed()
    {
        _refreshTokenStore
            .Setup(s => s.ConsumeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserId?)null);

        var result = await _sut.HandleAsync(
            new RefreshSessionCommand("token-que-nao-existe"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError!.Code.Should().Be("AUTH_REFRESH_TOKEN_INVALID");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_Should_ReturnInvalid_When_TokenIsMissing(string? token)
    {
        var result = await _sut.HandleAsync(new RefreshSessionCommand(token), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError!.Code.Should().Be("AUTH_REFRESH_TOKEN_INVALID");
        _refreshTokenStore.Verify(
            s => s.ConsumeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_ReturnInvalid_When_UserNoLongerExists()
    {
        GivenTokenBelongsTo(99);
        _userRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        var result = await _sut.HandleAsync(
            new RefreshSessionCommand("refresh-orfao"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.FirstError!.Code.Should().Be("AUTH_REFRESH_TOKEN_INVALID");
        _refreshTokenStore.Verify(
            s => s.IssueAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void GivenTokenBelongsTo(int userNumber) =>
        _refreshTokenStore
            .Setup(s => s.ConsumeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserId(TestIds.Of(userNumber)));

    private void GivenNewRefreshToken(string value) =>
        _refreshTokenStore
            .Setup(s => s.IssueAsync(It.IsAny<UserId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RefreshToken(value, Now.AddDays(30)));
}

public sealed class RevokeSessionCommandHandlerTests
{
    private readonly Mock<IRefreshTokenStore> _refreshTokenStore = new();
    private readonly RevokeSessionCommandHandler _sut;

    public RevokeSessionCommandHandlerTests() =>
        _sut = new RevokeSessionCommandHandler(_refreshTokenStore.Object);

    [Fact]
    public async Task HandleAsync_Should_RevokeToken_When_TokenIsProvided()
    {
        var result = await _sut.HandleAsync(new RevokeSessionCommand("refresh-xyz"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _refreshTokenStore.Verify(
            s => s.RevokeAsync("refresh-xyz", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task HandleAsync_Should_Succeed_When_TokenIsMissing(string? token)
    {
        // Logout e idempotente: o estado desejado (token sem valor) ja vale.
        var result = await _sut.HandleAsync(new RevokeSessionCommand(token), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        _refreshTokenStore.Verify(
            s => s.RevokeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
