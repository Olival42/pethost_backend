using PetHost.Modules.Owners.Application.Owners.Responses;

namespace PetHost.Modules.Owners.Application.Owners.RegisterOwnerAccount;

/// <summary>
/// Sessão aberta no cadastro mais o tutor completo (CPF e dados da conta): o cadastro
/// já entra na conta.
/// </summary>
/// <param name="ExpiresAt">Expiração do access token em Unix time (segundos).</param>
public sealed record OwnerSessionResponse(
    string AccessToken,
    string RefreshToken,
    long ExpiresAt,
    OwnerResponse Owner);
