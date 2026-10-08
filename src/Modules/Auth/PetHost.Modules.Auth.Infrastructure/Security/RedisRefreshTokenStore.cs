using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Domain.Users;
using StackExchange.Redis;

namespace PetHost.Modules.Auth.Infrastructure.Security;

/// <summary>
/// Refresh tokens no Redis. O dicionário de dados fecha em 14 tabelas e não prevê
/// uma para sessão; o Redis resolve sem criar a 15ª, e o TTL da chave faz a
/// expiração sozinho — não existe token vencido para limpar depois.
/// </summary>
/// <remarks>
/// A chave é o <b>SHA-256 do token</b>, nunca o token. Um dump do Redis entrega
/// hashes, que não servem para autenticar: é o mesmo raciocínio de não guardar
/// senha em texto.
/// </remarks>
internal sealed class RedisRefreshTokenStore(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<JwtOptions> options,
    TimeProvider timeProvider) : IRefreshTokenStore
{
    private const string KeyPrefix = "pethost:auth:refresh:";

    /// <summary>256 bits de entropia: inviável de adivinhar, curto o bastante para um header.</summary>
    private const int TokenSizeBytes = 32;

    private readonly JwtOptions _options = options.Value;

    public async Task<RefreshToken> IssueAsync(UserId userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var token = Base64Url.Encode(RandomNumberGenerator.GetBytes(TokenSizeBytes));
        var lifetime = TimeSpan.FromDays(_options.RefreshTokenLifetimeDays);

        await Database
            .StringSetAsync(
                KeyFor(token),
                userId.Value.ToString(),
                lifetime)
            .ConfigureAwait(false);

        return new RefreshToken(token, timeProvider.GetUtcNow().Add(lifetime));
    }

    public async Task<UserId?> ConsumeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(refreshToken))
            return null;

        // GETDEL: ler e apagar em um comando só. Duas chamadas concorrentes com o
        // mesmo token — o caso de um token vazado sendo usado em paralelo — não
        // podem as duas ter sucesso.
        var stored = await Database
            .StringGetDeleteAsync(KeyFor(refreshToken))
            .ConfigureAwait(false);

        if (stored.IsNullOrEmpty)
            return null;

        return Guid.TryParse(stored.ToString(), out var id)
            ? new UserId(id)
            : null;
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(refreshToken))
            return;

        await Database.KeyDeleteAsync(KeyFor(refreshToken)).ConfigureAwait(false);
    }

    private IDatabase Database => connectionMultiplexer.GetDatabase();

    private static RedisKey KeyFor(string token)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));

        return KeyPrefix + Base64Url.Encode(digest);
    }
}
