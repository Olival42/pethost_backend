using System.Globalization;
using Microsoft.Extensions.Options;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Domain.Users;
using StackExchange.Redis;

namespace PetHost.Modules.Auth.Infrastructure.Security;

/// <summary>
/// Guarda, por usuário, o instante (Unix, em milissegundos) da revogação:
/// <c>pethost:auth:access-revoked:&lt;id&gt;</c>. Token emitido antes desse instante é
/// recusado; token emitido depois dele vale.
/// </summary>
/// <remarks>
/// O TTL da chave é a vida do access token (mais a tolerância de relógio): depois
/// disso todo token anterior já expirou sozinho e a chave não serve para mais nada.
/// A comparação é em milissegundos (claim <c>iat_ms</c>), para que a sessão aberta logo
/// depois da revogação — a da troca de senha, ou um login em seguida — já valha. Token
/// sem <c>iat_ms</c> é comparado pelo <c>iat</c>, em segundos.
/// </remarks>
internal sealed class RedisAccessTokenRevocationStore(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<JwtOptions> options,
    TimeProvider timeProvider) : IAccessTokenRevocationStore
{
    private const string KeyPrefix = "pethost:auth:access-revoked:";

    /// <summary>Mesma tolerância de relógio da validação do JWT no host.</summary>
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    private readonly JwtOptions _options = options.Value;

    public async Task RevokeAllAsync(UserId userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var revokedAt = timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
        var ttl = TimeSpan.FromMinutes(_options.AccessTokenLifetimeMinutes) + ClockSkew;

        await connectionMultiplexer.GetDatabase()
            .StringSetAsync(KeyFor(userId), revokedAt.ToString(CultureInfo.InvariantCulture), ttl)
            .ConfigureAwait(false);
    }

    public async Task<bool> IsRevokedAsync(UserId userId, DateTimeOffset issuedAt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stored = await connectionMultiplexer.GetDatabase()
            .StringGetAsync(KeyFor(userId))
            .ConfigureAwait(false);

        return !stored.IsNullOrEmpty
            && long.TryParse(stored.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var revokedAt)
            && issuedAt.ToUnixTimeMilliseconds() < revokedAt;
    }

    private static RedisKey KeyFor(UserId userId) => KeyPrefix + userId.Value.ToString();
}
