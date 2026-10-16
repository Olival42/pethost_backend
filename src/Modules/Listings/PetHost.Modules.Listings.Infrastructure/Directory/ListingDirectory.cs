using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Listings.Domain.Listings;
using PetHost.Modules.Listings.Infrastructure.Persistence;
using PetHost.Shared.Contracts.Listings;

namespace PetHost.Modules.Listings.Infrastructure.Directory;

/// <summary>
/// Adaptador do contrato <see cref="IListingDirectory"/> (§5): a porta pela qual outro módulo
/// (o Bookings) lê as condições do cantinho. Leitura direta da tabela, sem o endereço exato.
/// </summary>
internal sealed class ListingDirectory(ListingsDbContext dbContext) : IListingDirectory
{
    public async Task<ListingReference?> GetByIdAsync(Guid listingId, CancellationToken cancellationToken)
    {
        var id = new ListingId(listingId);

        var listing = await dbContext.Listings
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return listing is null ? null : ToReference(listing);
    }

    public async Task<IReadOnlyList<ListingReference>> GetByIdsAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(listingIds);

        if (listingIds.Count == 0)
            return [];

        var ids = listingIds.Select(id => new ListingId(id)).ToList();

        var listings = await dbContext.Listings
            .AsNoTracking()
            .Where(l => ids.Contains(l.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. listings.Select(ToReference)];
    }

    public async Task<IReadOnlyDictionary<Guid, ListingAddressReference>> GetAddressesAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(listingIds);

        if (listingIds.Count == 0)
            return new Dictionary<Guid, ListingAddressReference>();

        var ids = listingIds.Select(id => new ListingId(id)).ToList();

        var listings = await dbContext.Listings
            .AsNoTracking()
            .Where(l => ids.Contains(l.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return listings.ToDictionary(
            l => l.Id.Value,
            l => new ListingAddressReference(
                l.Address.ZipCode,
                l.Address.Street,
                l.Address.Number,
                l.Address.Complement,
                l.Address.Neighborhood,
                l.Address.City,
                l.Address.State));
    }

    public async Task<IReadOnlyList<BlockedPeriod>> GetBlockedPeriodsAsync(Guid listingId, DateOnly from, CancellationToken cancellationToken)
    {
        var id = new ListingId(listingId);

        return await dbContext.AvailabilityBlocks
            .AsNoTracking()
            .Where(b => b.ListingId == id && b.EndDate >= from)
            .OrderBy(b => b.StartDate)
            .Select(b => new BlockedPeriod(b.StartDate, b.EndDate))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static ListingReference ToReference(Listing listing) => new(
        listing.Id.Value,
        listing.HostId,
        listing.Details.Title,
        listing.Address.Neighborhood,
        listing.Address.City,
        listing.Address.State,
        listing.IsActive,
        listing.Details.Capacity,
        listing.Details.NightlyRate,
        AcceptedSpeciesValues.ToWire(listing.Details.AcceptedSpecies),
        listing.Details.AcceptsSmall,
        listing.Details.AcceptsMedium,
        listing.Details.AcceptsLarge,
        [.. Enum.GetValues<DayOfWeek>().Where(day => WeekdaysValues.Includes(listing.Details.AvailableWeekdays, day))]);
}
