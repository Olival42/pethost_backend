using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Application.Abstractions;

/// <summary>
/// Porta de armazenamento do refresh token. A implementação usa Redis, com TTL
/// igual ao tempo de vida do token — expiração é responsabilidade do store.
/// </summary>
public interface IRefreshTokenStore
{
    /// <summary>Emite e guarda um novo refresh token para o usuário.</summary>
    Task<RefreshToken> IssueAsync(UserId userId, CancellationToken cancellationToken);

    /// <summary>
    /// Consome o token (uso único): devolve o usuário e invalida o token no mesmo
    /// passo. Devolve <c>null</c> se o token não existe, já foi usado ou expirou.
    /// </summary>
    Task<UserId?> ConsumeAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Invalida o token sem emitir outro. É o logout.</summary>
    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);
}
