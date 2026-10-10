namespace PetHost.Shared.Contracts.Hosts;

/// <summary>
/// Contrato público de leitura do módulo Hosts (§5). Outro módulo que guarda o id do
/// anfitrião — o Pets, nos pets da casa — usa isto para achar o anfitrião da conta logada
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
}

/// <summary>
/// Referência a um anfitrião: o id dele, o da conta e o nome a mostrar quando é empresa.
/// Sem CPF, CNPJ nem outro dado pessoal.
/// </summary>
/// <param name="PersonType"><c>individual</c> ou <c>company</c>.</param>
/// <param name="TradeName">Nome fantasia, só de empresa. Pessoa física usa o nome da conta.</param>
public sealed record HostReference(Guid Id, Guid UserId, string PersonType, string? TradeName, bool IsActive);
