using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Application.Abstractions;

/// <summary>
/// Porta do token de "esqueci a senha". A implementação usa Redis com TTL igual à
/// validade do token — expiração é responsabilidade do store.
/// </summary>
public interface IPasswordResetTokenStore
{
    /// <summary>
    /// Emite um token novo para o usuário. Um token anterior ainda não usado deixa
    /// de valer: só o e-mail mais recente funciona.
    /// </summary>
    Task<PasswordResetToken> IssueAsync(UserId userId, CancellationToken cancellationToken);

    /// <summary>
    /// Consome o token (uso único): devolve o dono e invalida o token no mesmo passo.
    /// Devolve <c>null</c> se o token não existe, já foi usado, expirou ou foi
    /// substituído por um pedido mais novo.
    /// </summary>
    Task<UserId?> ConsumeAsync(string token, CancellationToken cancellationToken);
}

/// <param name="Value">Valor opaco que vai no e-mail. Só o hash fica guardado.</param>
public sealed record PasswordResetToken(string Value, DateTimeOffset ExpiresAt);
