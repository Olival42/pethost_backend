using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Modules.Auth.Infrastructure.Security;
using PetHost.TestKit;
using Xunit;

namespace PetHost.Modules.Auth.Infrastructure.UnitTests.Security;

public sealed class JwtAccessTokenGeneratorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly JwtOptions Options = new()
    {
        Issuer = "https://api.pethost.test",
        Audience = "pethost-app",
        Key = "chave-de-teste-com-mais-de-32-bytes-para-hmac-sha256",
        AccessTokenLifetimeMinutes = 15,
        RefreshTokenLifetimeDays = 30,
    };

    private readonly FakeTimeProvider _timeProvider = new(Now);
    private readonly JwtAccessTokenGenerator _sut;

    public JwtAccessTokenGeneratorTests() =>
        _sut = new JwtAccessTokenGenerator(Microsoft.Extensions.Options.Options.Create(Options), _timeProvider);

    [Fact]
    public void Generate_Should_ReturnExpiryFromTimeProvider_When_Called()
    {
        var token = _sut.Generate(new UserBuilder().WithId(TestIds.Of(42)).Build());

        token.ExpiresAt.Should().Be(Now.AddMinutes(15));
        token.ExpiresInSeconds.Should().Be(900);
    }

    [Fact]
    public void Generate_Should_CarryUserIdentityClaims_When_Called()
    {
        var user = new UserBuilder()
            .WithId(TestIds.Of(42))
            .WithFullName("Camila Souza")
            .WithEmail("camila@exemplo.com")
            .WithRole(UserRole.Owner)
            .Build();

        var claims = ReadClaims(_sut.Generate(user).Value);

        claims[JwtClaimNames.Subject].Should().Be(TestIds.Of(42).ToString());
        claims[JwtClaimNames.Name].Should().Be("Camila Souza");
        claims[JwtClaimNames.Email].Should().Be("camila@exemplo.com");
        claims[JwtClaimNames.Role].Should().Be("owner");
    }

    [Theory]
    [InlineData(UserRole.Owner, "owner")]
    [InlineData(UserRole.Host, "host")]
    [InlineData(UserRole.Admin, "admin")]
    public void Generate_Should_PutWireRoleInRoleClaim_When_RoleVaries(UserRole role, string expected)
    {
        var user = new UserBuilder().WithId(TestIds.Of(1)).WithRole(role).Build();

        ReadClaims(_sut.Generate(user).Value)[JwtClaimNames.Role].Should().Be(expected);
    }

    [Fact]
    public void Generate_Should_SetIssuerAndAudience_When_Called()
    {
        var jwt = ReadToken(_sut.Generate(new UserBuilder().WithId(TestIds.Of(1)).Build()).Value);

        jwt.Issuer.Should().Be("https://api.pethost.test");
        jwt.Audiences.Should().ContainSingle().Which.Should().Be("pethost-app");
    }

    [Fact]
    public void Generate_Should_NeverIncludeThePasswordHash_When_Called()
    {
        var user = new UserBuilder().WithId(TestIds.Of(1)).WithPasswordHash(UserBuilder.SampleHash).Build();

        var token = _sut.Generate(user);

        token.Value.Should().NotContain("argon2");
        ReadClaims(token.Value).Values.Should().NotContain(UserBuilder.SampleHash);
    }

    [Fact]
    public void Generate_Should_ProduceUniqueTokenId_When_CalledTwice()
    {
        var user = new UserBuilder().WithId(TestIds.Of(1)).Build();

        var first = ReadClaims(_sut.Generate(user).Value)[JwtClaimNames.TokenId];
        var second = ReadClaims(_sut.Generate(user).Value)[JwtClaimNames.TokenId];

        first.Should().NotBe(second);
    }

    [Fact]
    public async Task Generate_Should_ProduceTokenThatValidates_When_UsingTheSameKey()
    {
        var token = _sut.Generate(new UserBuilder().WithId(TestIds.Of(42)).Build());

        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(
            token.Value,
            new TokenValidationParameters
            {
                ValidIssuer = Options.Issuer,
                ValidAudience = Options.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Options.Key)),
                ValidateLifetime = true,
                LifetimeValidator = (_, expires, _, _) => expires > Now.UtcDateTime,
            });

        validation.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Generate_Should_ProduceTokenThatFailsValidation_When_KeyDiffers()
    {
        var token = _sut.Generate(new UserBuilder().WithId(TestIds.Of(42)).Build());

        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(
            token.Value,
            new TokenValidationParameters
            {
                ValidIssuer = Options.Issuer,
                ValidAudience = Options.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes("outra-chave-completamente-diferente-com-32-bytes")),
                ValidateLifetime = false,
            });

        validation.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Generate_Should_ReflectConfiguredLifetime_When_OptionsChange()
    {
        var generator = new JwtAccessTokenGenerator(
            Microsoft.Extensions.Options.Options.Create(new JwtOptions
            {
                Issuer = Options.Issuer,
                Audience = Options.Audience,
                Key = Options.Key,
                AccessTokenLifetimeMinutes = 60,
                RefreshTokenLifetimeDays = 7,
            }),
            _timeProvider);

        var token = generator.Generate(new UserBuilder().WithId(TestIds.Of(1)).Build());

        token.ExpiresInSeconds.Should().Be(3600);
        token.ExpiresAt.Should().Be(Now.AddHours(1));
    }

    private static JsonWebToken ReadToken(string token) => new JsonWebTokenHandler().ReadJsonWebToken(token);

    private static Dictionary<string, string> ReadClaims(string token) =>
        ReadToken(token).Claims.ToDictionary(c => c.Type, c => c.Value, StringComparer.Ordinal);
}
