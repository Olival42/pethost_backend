using PetHost.Shared.Contracts.Accounts;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Hosts.Application.Abstractions;

/// <summary>
/// Porta para a conta do anfitrião, que vive no módulo Auth (§5: a Application declara a
/// interface; a Infrastructure implementa via <c>Shared.Contracts</c>).
/// </summary>
public interface IHostAccounts
{
    /// <summary>Contas pelos ids, numa consulta só. Id sem conta não aparece.</summary>
    Task<IReadOnlyList<AccountSummary>> GetByIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken);

    /// <summary>Erros do patch de perfil da conta, conferido contra o perfil atual. Sem gravar.</summary>
    Task<Result> ValidateProfileAsync(Guid userId, AccountProfilePatch patch, CancellationToken cancellationToken);

    /// <summary>Aplica o patch na conta. Devolve o perfil anterior, para desfazer.</summary>
    Task<Result<AccountProfilePatch>> UpdateProfileAsync(Guid userId, AccountProfilePatch patch, CancellationToken cancellationToken);

    /// <summary>Inativa a conta e derruba as sessões dela.</summary>
    Task<Result> DeactivateAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Reativa a conta de anfitrião com as credenciais do login e abre a sessão.</summary>
    Task<Result<AccountSession>> ReactivateAsync(string? email, string? password, CancellationToken cancellationToken);

    /// <summary>Suspende a conta (admin) e derruba as sessões.</summary>
    Task<Result> SuspendAsync(Guid userId, string? reason, Guid adminId, CancellationToken cancellationToken);

    /// <summary>Tira a suspensão da conta (admin).</summary>
    Task<Result> LiftSuspensionAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>A senha confere com a da conta?</summary>
    Task<bool> VerifyPasswordAsync(Guid userId, string? password, CancellationToken cancellationToken);

    /// <summary>Exclui a conta (LGPD): anonimiza, inutiliza a senha e derruba as sessões. Sem volta.</summary>
    Task<Result> EraseAsync(Guid userId, CancellationToken cancellationToken);
}
