using PetHost.Modules.Pets.Domain.Pets;

namespace PetHost.Modules.Pets.Application.Abstractions;

/// <summary>
/// Porta para quem pode ter pet — o tutor (módulo Owners) e o anfitrião (módulo Hosts) — e
/// para os dados da conta deles (Auth). §5: a Application declara a interface; a
/// Infrastructure implementa via <c>Shared.Contracts</c>.
/// </summary>
public interface IPetKeepers
{
    /// <summary>
    /// O tutor ou o anfitrião da conta logada, conforme o papel do token
    /// (<c>owner</c> → tutor, <c>host</c> → anfitrião). <c>null</c> se a conta ainda não tem
    /// esse perfil, ou se o papel não tem pets (admin).
    /// </summary>
    Task<PetKeeper?> FindByAccountAsync(Guid userId, string role, CancellationToken cancellationToken);

    /// <summary>Nome e foto de cada tutor/anfitrião, numa consulta por módulo. Quem sumiu não aparece.</summary>
    Task<IReadOnlyDictionary<PetKeeper, KeeperSummary>> GetSummariesAsync(
        IReadOnlyCollection<PetKeeper> keepers,
        CancellationToken cancellationToken);

    /// <summary>Existe anfitrião com esse id?</summary>
    Task<bool> HostExistsAsync(Guid hostId, CancellationToken cancellationToken);
}
