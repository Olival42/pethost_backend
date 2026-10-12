namespace PetHost.Modules.Listings.Application.Abstractions;

/// <summary>
/// Porta para o anfitrião (módulo Hosts) e para os dados da conta dele (Auth). §5: a
/// Application declara a interface; a Infrastructure implementa via <c>Shared.Contracts</c>.
/// </summary>
public interface IListingHosts
{
    /// <summary>O anfitrião da conta <paramref name="userId"/>, ou <c>null</c> se ela não tem perfil de anfitrião.</summary>
    Task<HostProfile?> FindByAccountAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Nome e foto de cada anfitrião, numa consulta por módulo. Quem sumiu não aparece.</summary>
    Task<IReadOnlyDictionary<Guid, ListingHostSummary>> GetSummariesAsync(
        IReadOnlyCollection<Guid> hostIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Anfitriões inativos, suspensos ou ainda sem repasses liberados pelo Stripe: os cantinhos
    /// deles não aparecem para os outros.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetUnavailableIdsAsync(CancellationToken cancellationToken);
}

/// <summary>O anfitrião da conta logada.</summary>
/// <param name="Id">Id do anfitrião (<c>host.hosts.id</c>), nunca o da conta.</param>
/// <param name="IsCompany">Pessoa jurídica: pode ter vários cantinhos (um por filial).</param>
/// <param name="IsAvailable">Ativo e sem suspensão.</param>
/// <param name="PayoutsEnabled">O Stripe aprovou o cadastro de recebimento (repasses liberados). Sem isso não cria cantinho.</param>
public sealed record HostProfile(Guid Id, bool IsCompany, bool IsAvailable, bool PayoutsEnabled = false);
