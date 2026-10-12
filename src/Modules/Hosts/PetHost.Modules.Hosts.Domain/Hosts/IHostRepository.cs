namespace PetHost.Modules.Hosts.Domain.Hosts;

/// <summary>Acesso a <see cref="Host"/>. A Application só conhece esta interface (§11).</summary>
public interface IHostRepository
{
    /// <summary>Pelo id do anfitrião (rotas do admin). Rastreado.</summary>
    Task<Host?> GetByIdAsync(HostId id, CancellationToken cancellationToken);

    /// <summary>Pelo id da conta (rotas <c>me</c>, o id vem do token). Rastreado.</summary>
    Task<Host?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Pela conta conectada no Stripe (<c>acct_...</c>), para o webhook. Rastreado.</summary>
    Task<Host?> GetByStripeAccountIdAsync(string stripeAccountId, CancellationToken cancellationToken);

    /// <summary>
    /// Para a manutenção do cadastro de recebimento (rastreados): anfitriões ativos e sem suspensão
    /// que ainda não recebem (para sincronizar e lembrar) e os que recebem mas não são lidos no
    /// Stripe desde <paramref name="staleBefore"/> (para pegar um bloqueio cujo webhook se perdeu).
    /// </summary>
    Task<IReadOnlyList<Host>> ListForPayoutsMaintenanceAsync(DateTimeOffset staleBefore, CancellationToken cancellationToken);

    /// <summary>Todos os anfitriões, do mais novo para o mais antigo. Lista administrativa (§13).</summary>
    Task<IReadOnlyList<Host>> ListAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Há anfitrião pessoa física com este CPF? (único entre pessoas físicas; CPF liberado pelo
    /// admin não conta)
    /// </summary>
    Task<bool> ExistsIndividualByCpfAsync(Cpf cpf, CancellationToken cancellationToken);

    /// <summary>Há anfitrião (empresa) com este CNPJ? CNPJ liberado pelo admin não conta.</summary>
    Task<bool> ExistsByCnpjAsync(Cnpj cnpj, CancellationToken cancellationToken);

    void Add(Host host);
}
