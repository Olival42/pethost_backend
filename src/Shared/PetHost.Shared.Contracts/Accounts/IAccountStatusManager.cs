using PetHost.Shared.Kernel.Results;

namespace PetHost.Shared.Contracts.Accounts;

/// <summary>
/// Contrato público do módulo Auth para mudar o status de uma conta (§5). O módulo de
/// um papel usa isto para mudar o status do perfil e da conta juntos.
/// </summary>
/// <remarks>
/// Dois status diferentes: <b>inativa</b> é decisão da pessoa, e ela volta com a senha;
/// <b>suspensa</b> é decisão do admin, e só o admin tira.
/// </remarks>
public interface IAccountStatusManager
{
    /// <summary>Inativa a conta e derruba todas as sessões dela na hora.</summary>
    Task<Result> DeactivateAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Confere as credenciais como o login, reativa a conta e abre a sessão. Credenciais
    /// erradas devolvem o mesmo 401 do login; conta suspensa, 403.
    /// </summary>
    Task<Result<AccountSession>> ReactivateAsync(
        string? email,
        string? password,
        string role,
        CancellationToken cancellationToken);

    /// <summary>Suspende a conta (admin, com motivo) e derruba as sessões. O admin não pode ser suspenso.</summary>
    Task<Result> SuspendAsync(Guid userId, string? reason, Guid adminId, CancellationToken cancellationToken);

    /// <summary>Tira a suspensão. Idempotente.</summary>
    Task<Result> LiftSuspensionAsync(Guid userId, CancellationToken cancellationToken);
}
