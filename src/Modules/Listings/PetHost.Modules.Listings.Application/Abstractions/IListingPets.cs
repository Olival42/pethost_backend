using PetHost.Modules.Listings.Domain.Listings;

namespace PetHost.Modules.Listings.Application.Abstractions;

/// <summary>
/// Porta para os pets (módulo Pets) e para o tutor da conta logada (Owners), usada no "Combina
/// com seu pet". §5: a Application declara a interface; a Infrastructure implementa via
/// <c>Shared.Contracts</c>.
/// </summary>
public interface IListingPets
{
    /// <summary>O id do tutor (<c>owner.owners.id</c>) da conta <paramref name="userId"/>, ou <c>null</c> se ela não tem perfil de tutor.</summary>
    Task<Guid?> FindOwnerIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Pets pelos ids, ativos ou não, numa consulta só. Id que não existe não aparece.</summary>
    Task<IReadOnlyList<ListingPet>> GetByIdsAsync(IReadOnlyCollection<Guid> petIds, CancellationToken cancellationToken);
}

/// <summary>Um pet com o dono, para o "Combina com seu pet".</summary>
/// <param name="OwnerId">Tutor dono; nulo quando o pet é de um anfitrião.</param>
/// <param name="Profile">O que a regra lê da ficha.</param>
public sealed record ListingPet(Guid? OwnerId, bool IsActive, CompatibilityPet Profile);
