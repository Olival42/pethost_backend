using PetHost.Shared.Contracts.Accounts;

namespace PetHost.Modules.Hosts.Application.Hosts.Responses;

/// <summary>Sessão aberta na reativação: os tokens, a conta e o perfil de anfitrião (se já aprovado).</summary>
/// <param name="ExpiresAt">Expiração do access token em Unix time (segundos).</param>
/// <param name="Host">Perfil de anfitrião. Nulo se a conta ainda não foi aprovada na verificação.</param>
public sealed record HostSessionResponse(
    string AccessToken,
    string RefreshToken,
    long ExpiresAt,
    HostResponse? Host,
    AccountSummary User);
