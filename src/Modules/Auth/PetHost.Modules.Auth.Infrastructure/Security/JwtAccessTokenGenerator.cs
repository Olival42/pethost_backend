using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Infrastructure.Security;

/// <summary>Emite o access token como JWT assinado em HMAC-SHA256.</summary>
internal sealed class JwtAccessTokenGenerator : IAccessTokenGenerator
{
    private static readonly JsonWebTokenHandler TokenHandler = new();

    private readonly JwtOptions _options;
    private readonly SigningCredentials _signingCredentials;
    private readonly TimeProvider _timeProvider;

    public JwtAccessTokenGenerator(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;

        // A chave é derivada uma vez: criar SymmetricSecurityKey por requisição
        // é desperdício em um caminho chamado a cada login.
        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
            SecurityAlgorithms.HmacSha256);
    }

    public AccessToken Generate(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = _signingCredentials,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [JwtClaimNames.Subject] = user.Id.Value.ToString(),
                [JwtClaimNames.Email] = user.Email.Value,
                [JwtClaimNames.Name] = user.FullName.Value,
                [JwtClaimNames.Role] = UserRoleValues.ToWire(user.Role),
                [JwtClaimNames.TokenId] = Guid.NewGuid().ToString(),
                [JwtClaimNames.IssuedAtMilliseconds] = now.ToUnixTimeMilliseconds(),
            },
        };

        return new AccessToken(
            TokenHandler.CreateToken(descriptor),
            expiresAt,
            (long)(expiresAt - now).TotalSeconds);
    }
}
