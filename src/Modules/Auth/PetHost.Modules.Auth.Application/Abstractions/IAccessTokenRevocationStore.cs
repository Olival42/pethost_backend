using PetHost.Modules.Auth.Domain.Users;

namespace PetHost.Modules.Auth.Application.Abstractions;

/// <summary>
/// Revogação de access token. O JWT não tem estado, então "revogar" é guardar o
/// instante a partir do qual os tokens já emitidos para o usuário deixam de valer;
/// o host confere isso a cada request autenticada.
/// </summary>
public interface IAccessTokenRevocationStore
{
    /// <summary>
    /// Invalida todos os access tokens do usuário emitidos até agora. Usado ao
    /// inativar a conta e ao trocar a senha.
    /// </summary>
    Task RevokeAllAsync(UserId userId, CancellationToken cancellationToken);

    /// <summary>O token emitido em <paramref name="issuedAt"/> foi revogado?</summary>
    Task<bool> IsRevokedAsync(UserId userId, DateTimeOffset issuedAt, CancellationToken cancellationToken);
}
