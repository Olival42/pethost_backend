namespace PetHost.Shared.Contracts.Hosts;

/// <summary>
/// Contrato público de leitura do módulo Hosts (§5). Outro módulo que guarda o id do
/// anfitrião — o Pets, nos pets da casa; o Listings, nos cantinhos — usa isto para achar o anfitrião da conta logada
/// e para conferir anfitriões, sem referenciar o Hosts nem fazer JOIN entre schemas.
/// </summary>
public interface IHostDirectory
{
    /// <summary>O anfitrião da conta <paramref name="userId"/>, ou <c>null</c> se ela não tem perfil de anfitrião.</summary>
    Task<HostReference?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Anfitriões pelos ids, numa consulta só. Id que não existe não aparece.</summary>
    Task<IReadOnlyList<HostReference>> GetByIdsAsync(
        IReadOnlyCollection<Guid> hostIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ids dos anfitriões que não podem aparecer para os outros: inativos, suspensos ou ainda sem
    /// os repasses liberados pelo Stripe (cadastro de recebimento não concluído). A busca de
    /// cantinhos tira estes da lista.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetUnavailableIdsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Referência a um anfitrião: o id dele, o da conta e o nome a mostrar quando é empresa.
/// Sem CPF, CNPJ nem outro dado pessoal.
/// </summary>
/// <param name="PersonType"><c>pf</c> (pessoa física) ou <c>pj</c> (pessoa jurídica).</param>
/// <param name="TradeName">Nome fantasia, só de empresa. Pessoa física usa o nome da conta.</param>
/// <param name="IsSuspended">Suspenso pelo admin. Diferente de inativo: só o admin tira.</param>
/// <param name="PayoutsEnabled">O Stripe liberou os repasses (cadastro de recebimento concluído).</param>
public sealed record HostReference(
    Guid Id,
    Guid UserId,
    string PersonType,
    string? TradeName,
    bool IsActive,
    bool IsSuspended = false,
    bool PayoutsEnabled = false)
{
    /// <summary>Valor de <see cref="PersonType"/> da pessoa jurídica.</summary>
    public const string CompanyPersonType = "pj";

    /// <summary>Pessoa jurídica (empresa).</summary>
    public bool IsCompany => PersonType == CompanyPersonType;

    /// <summary>Ativo e sem suspensão: pode usar a conta (cadastrar cantinho, pets da casa).</summary>
    public bool IsAvailable => IsActive && !IsSuspended;

    /// <summary>
    /// Pode aparecer para os outros: ativo, sem suspensão e com os repasses liberados pelo Stripe
    /// (só assim dá para pagar uma reserva com ele).
    /// </summary>
    public bool IsListable => IsAvailable && PayoutsEnabled;
}
