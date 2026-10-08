using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Domain.Users;
using StackExchange.Redis;

namespace PetHost.Modules.Auth.Infrastructure.Security;

/// <summary>
/// Tokens de troca de senha no Redis, no mesmo molde do refresh token: guarda o
/// <b>SHA-256 do token</b>, nunca o token, e o TTL faz a expiração.
/// </summary>
/// <remarks>
/// Duas chaves por pedido:
/// <list type="bullet">
/// <item><c>pethost:auth:password-reset:&lt;hash&gt;</c> → id do usuário (o token em si).</item>
/// <item><c>pethost:auth:password-reset:user:&lt;id&gt;</c> → hash do token vigente, para
/// um pedido novo invalidar o anterior.</item>
/// </list>
/// </remarks>
internal sealed class RedisPasswordResetTokenStore(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<PasswordResetOptions> options,
    TimeProvider timeProvider) : IPasswordResetTokenStore
{
    private const string KeyPrefix = "pethost:auth:password-reset:";
    private const string UserKeyPrefix = KeyPrefix + "user:";

    /// <summary>256 bits de entropia: inviável de adivinhar dentro da validade.</summary>
    private const int TokenSizeBytes = 32;

    private readonly PasswordResetOptions _options = options.Value;

    public async Task<PasswordResetToken> IssueAsync(UserId userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var token = Base64Url.Encode(RandomNumberGenerator.GetBytes(TokenSizeBytes));
        var tokenKey = TokenKeyFor(token);
        var userKey = UserKeyFor(userId);
        var lifetime = TimeSpan.FromMinutes(_options.TokenLifetimeMinutes);

        // Troca o ponteiro do usuário para o token novo e apaga o antigo.
        var previousTokenKey = await Database
            .StringSetAndGetAsync(userKey, tokenKey.ToString(), lifetime)
            .ConfigureAwait(false);

        if (!previousTokenKey.IsNullOrEmpty)
            await Database.KeyDeleteAsync(previousTokenKey.ToString()).ConfigureAwait(false);

        await Database
            .StringSetAsync(tokenKey, userId.Value.ToString(), lifetime)
            .ConfigureAwait(false);

        return new PasswordResetToken(token, timeProvider.GetUtcNow().Add(lifetime));
    }

    public async Task<UserId?> ConsumeAsync(string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(token))
            return null;

        // GETDEL: duas trocas simultâneas com o mesmo token não podem as duas valer.
        var stored = await Database
            .StringGetDeleteAsync(TokenKeyFor(token))
            .ConfigureAwait(false);

        if (stored.IsNullOrEmpty || !Guid.TryParse(stored.ToString(), out var id))
            return null;

        var userId = new UserId(id);
        await Database.KeyDeleteAsync(UserKeyFor(userId)).ConfigureAwait(false);

        return userId;
    }

    private IDatabase Database => connectionMultiplexer.GetDatabase();

    private static RedisKey TokenKeyFor(string token)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));

        return KeyPrefix + Base64Url.Encode(digest);
    }

    private static RedisKey UserKeyFor(UserId userId) => UserKeyPrefix + userId.Value.ToString();
}
