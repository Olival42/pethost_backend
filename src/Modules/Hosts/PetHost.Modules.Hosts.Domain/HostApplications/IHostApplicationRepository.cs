using PetHost.Modules.Hosts.Domain.Hosts;

namespace PetHost.Modules.Hosts.Domain.HostApplications;

/// <summary>Acesso a <see cref="HostApplication"/>. A Application só conhece esta interface (§11).</summary>
public interface IHostApplicationRepository
{
    /// <summary>Pelo id, com as fotos. Rastreado.</summary>
    Task<HostApplication?> GetByIdAsync(HostApplicationId id, CancellationToken cancellationToken);

    /// <summary>
    /// Todas as solicitações da conta, da mais nova para a mais antiga, com as fotos.
    /// Rastreadas (a aberta, se houver, pode ser alterada).
    /// </summary>
    Task<IReadOnlyList<HostApplication>> ListByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Há solicitação aberta de <b>outra</b> conta com este CPF?</summary>
    Task<bool> ExistsOpenWithCpfAsync(Cpf cpf, Guid exceptUserId, CancellationToken cancellationToken);

    /// <summary>Há solicitação aberta de <b>outra</b> conta com este CNPJ?</summary>
    Task<bool> ExistsOpenWithCnpjAsync(Cnpj cnpj, Guid exceptUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Solicitações num status, sem paginação (lista administrativa, §13). A fila
    /// (<c>submitted</c>) vem da mais antiga para a mais nova: quem pediu primeiro é atendido
    /// primeiro. Os outros status, da mais nova para a mais antiga.
    /// </summary>
    Task<IReadOnlyList<HostApplication>> ListByStatusAsync(HostApplicationStatus status, CancellationToken cancellationToken);

    /// <summary>Rascunhos criados antes de <paramref name="createdBefore"/>, com as fotos. Rastreados.</summary>
    Task<IReadOnlyList<HostApplication>> ListDraftsCreatedBeforeAsync(DateTimeOffset createdBefore, CancellationToken cancellationToken);

    /// <summary>
    /// Solicitações fechadas até <paramref name="closedBefore"/> que ainda têm fotos. Rastreadas.
    /// </summary>
    Task<IReadOnlyList<HostApplication>> ListWithDocumentsClosedBeforeAsync(DateTimeOffset closedBefore, CancellationToken cancellationToken);

    void Add(HostApplication application);
}
