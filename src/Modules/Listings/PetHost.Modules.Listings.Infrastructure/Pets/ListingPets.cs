using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Shared.Contracts.Owners;
using PetHost.Shared.Contracts.Pets;

namespace PetHost.Modules.Listings.Infrastructure.Pets;

/// <summary>
/// Adaptador da porta <see cref="IListingPets"/> sobre os contratos públicos:
/// <see cref="IOwnerDirectory"/> (tutor da conta) e <see cref="IPetDirectory"/> (ficha do pet).
/// </summary>
internal sealed class ListingPets(
    IOwnerDirectory ownerDirectory,
    IPetDirectory petDirectory) : IListingPets
{
    public async Task<Guid?> FindOwnerIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var owner = await ownerDirectory.GetByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);

        return owner?.Id;
    }

    public async Task<IReadOnlyList<ListingPet>> GetByIdsAsync(IReadOnlyCollection<Guid> petIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(petIds);

        if (petIds.Count == 0)
            return [];

        var pets = await petDirectory.GetByIdsAsync(petIds, cancellationToken).ConfigureAwait(false);

        return [.. pets.Select(pet => new ListingPet(
            pet.OwnerId,
            pet.IsActive,
            new CompatibilityPet(pet.Id, pet.Name, pet.Species, pet.Size, pet.GoodWithDogs, pet.GoodWithCats, pet.GoodWithKids)))];
    }
}
