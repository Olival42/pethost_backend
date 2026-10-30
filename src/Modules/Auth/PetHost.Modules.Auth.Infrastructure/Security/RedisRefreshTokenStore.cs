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
/// <para>
/// Cada usuário tem também um conjunto <c>pethost:auth:refresh:user:&lt;id&gt;</c> com as
/// chaves dos tokens dele, para a troca de senha conseguir derrubar todas as sessões.
/// Membros cujo token já expirou ficam no conjunto até ele expirar também; apagar
/// uma chave que não existe é inofensivo.
/// </para>
/// </remarks>
internal sealed class RedisRefreshTokenStore(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<JwtOptions> options,
    TimeProvider timeProvider) : IRefreshTokenStore
{
    private const string KeyPrefix = "pethost:auth:refresh:";
    private const string UserSetPrefix = KeyPrefix + "user:";

    /// <summary>256 bits de entropia: inviável de adivinhar, curto o bastante para um header.</summary>
    private const int TokenSizeBytes = 32;

    private readonly JwtOptions _options = options.Value;

    public async Task<RefreshToken> IssueAsync(UserId userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var token = Base64Url.Encode(RandomNumberGenerator.GetBytes(TokenSizeBytes));
        var lifetime = TimeSpan.FromDays(_options.RefreshTokenLifetimeDays);

        var tokenKey = KeyFor(token);
        var userSet = UserSetFor(userId);

        await Database
            .StringSetAsync(tokenKey, userId.Value.ToString(), lifetime)
            .ConfigureAwait(false);

        // O conjunto vive tanto quanto o token mais novo dele.
        await Database.SetAddAsync(userSet, tokenKey.ToString()).ConfigureAwait(false);
        await Database.KeyExpireAsync(userSet, lifetime).ConfigureAwait(false);

        return new RefreshToken(token, timeProvider.GetUtcNow().Add(lifetime));
    }

    public async Task<UserId?> ConsumeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(refreshToken))
            return null;

        return await TakeAsync(KeyFor(refreshToken)).ConfigureAwait(false);
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(refreshToken))
            return;

        await TakeAsync(KeyFor(refreshToken)).ConfigureAwait(false);
    }

    public async Task RevokeAllAsync(UserId userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var userSet = UserSetFor(userId);
        var tokenKeys = await Database.SetMembersAsync(userSet).ConfigureAwait(false);

        var keys = tokenKeys
            .Where(member => !member.IsNullOrEmpty)
            .Select(member => (RedisKey)member.ToString())
            .Append(userSet)
            .ToArray();

        await Database.KeyDeleteAsync(keys).ConfigureAwait(false);
    }

    /// <summary>
    /// Lê e apaga o token num comando só (GETDEL) e tira a chave do conjunto do dono.
    /// Duas chamadas concorrentes com o mesmo token — o caso de um token vazado sendo
    /// usado em paralelo — não podem as duas ter sucesso.
    /// </summary>
    private async Task<UserId?> TakeAsync(RedisKey tokenKey)
    {
        var stored = await Database.StringGetDeleteAsync(tokenKey).ConfigureAwait(false);

        if (stored.IsNullOrEmpty || !Guid.TryParse(stored.ToString(), out var id))
            return null;

        var userId = new UserId(id);
        await Database.SetRemoveAsync(UserSetFor(userId), tokenKey.ToString()).ConfigureAwait(false);

        return userId;
    }

    private IDatabase Database => connectionMultiplexer.GetDatabase();

    private static RedisKey UserSetFor(UserId userId) => UserSetPrefix + userId.Value.ToString();

    private static RedisKey KeyFor(string token)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));

        return KeyPrefix + Base64Url.Encode(digest);
    }
}
