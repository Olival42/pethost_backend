using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Pets.Domain.Pets;

namespace PetHost.Modules.Pets.Infrastructure.Persistence.Repositories;

/// <summary>Implementação de <see cref="IPetRepository"/> sobre o EF Core.</summary>
internal sealed class PetRepository(PetsDbContext dbContext) : IPetRepository
{
    public Task<Pet?> GetByIdAsync(PetId id, CancellationToken cancellationToken) =>
        dbContext.Pets
            .Include(PetsDbContext.PhotosField)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Pet>> ListByKeeperAsync(PetKeeper keeper, CancellationToken cancellationToken) =>
        await ByKeeper(keeper)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<Pet>> ListActiveByKeeperAsync(PetKeeper keeper, CancellationToken cancellationToken) =>
        await ByKeeper(keeper)
            .Where(p => p.IsActive)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<bool> MicrochipInUseAsync(string microchip, PetId? exceptPetId, CancellationToken cancellationToken)
    {
        var pets = dbContext.Pets.Where(p => p.IsActive && p.Profile.Microchip == microchip);

        if (exceptPetId is { } petId)
            pets = pets.Where(p => p.Id != petId);

        return pets.AnyAsync(cancellationToken);
    }

    public void Add(Pet pet) => dbContext.Pets.Add(pet);

    /// <summary>Filtra pela coluna do tipo do dono — a que tem índice.</summary>
    private IQueryable<Pet> ByKeeper(PetKeeper keeper)
    {
        var pets = dbContext.Pets.AsNoTracking().Include(PetsDbContext.PhotosField);

        return keeper.Type == KeeperType.Owner
            ? pets.Where(p => p.OwnerId == keeper.Id)
            : pets.Where(p => p.HostId == keeper.Id);
    }
}
