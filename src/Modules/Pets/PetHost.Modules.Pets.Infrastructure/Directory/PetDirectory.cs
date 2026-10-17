using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Pets.Domain.Pets;
using PetHost.Modules.Pets.Infrastructure.Persistence;
using PetHost.Shared.Contracts.Pets;

namespace PetHost.Modules.Pets.Infrastructure.Directory;

/// <summary>
/// Adaptador do contrato <see cref="IPetDirectory"/> (§5): a porta pela qual outro módulo (o
/// Bookings) confere os pets de uma reserva, e o Listings cruza a ficha com o cantinho. Leitura direta da tabela, só o básico da ficha.
/// </summary>
internal sealed class PetDirectory(PetsDbContext dbContext) : IPetDirectory
{
    public async Task<IReadOnlyList<PetReference>> GetByIdsAsync(
        IReadOnlyCollection<Guid> petIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(petIds);

        if (petIds.Count == 0)
            return [];

        var ids = petIds.Select(id => new PetId(id)).ToList();

        var pets = await dbContext.Pets
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. pets.Select(pet => new PetReference(
            pet.Id.Value,
            pet.OwnerId,
            pet.Profile.Name,
            PetSpeciesValues.ToWire(pet.Profile.Species),
            pet.Profile.Size is { } size ? PetSizeValues.ToWire(size) : null,
            pet.IsActive,
            pet.Profile.GoodWithDogs,
            pet.Profile.GoodWithCats,
            pet.Profile.GoodWithKids))];
    }
}
