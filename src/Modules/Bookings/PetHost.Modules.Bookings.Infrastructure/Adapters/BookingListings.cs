using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Shared.Contracts.Hosts;
using PetHost.Shared.Contracts.Listings;

namespace PetHost.Modules.Bookings.Infrastructure.Adapters;

/// <summary>
/// Adaptador da porta <see cref="IBookingListings"/> sobre os contratos públicos:
/// <see cref="IListingDirectory"/> (o cantinho) e <see cref="IHostDirectory"/> (o anfitrião pode
/// receber reservas?).
/// </summary>
internal sealed class BookingListings(
    IListingDirectory listingDirectory,
    IHostDirectory hostDirectory) : IBookingListings
{
    public async Task<BookableListing?> FindAsync(Guid listingId, CancellationToken cancellationToken)
    {
        var listing = await listingDirectory.GetByIdAsync(listingId, cancellationToken).ConfigureAwait(false);
        if (listing is null)
            return null;

        var hosts = await hostDirectory.GetByIdsAsync([listing.HostId], cancellationToken).ConfigureAwait(false);
        var host = hosts.FirstOrDefault(h => h.Id == listing.HostId);

        // Mesmo critério da busca: cantinho ativo de anfitrião ativo, sem suspensão e com repasses.
        var isBookable = listing.IsActive && host is { IsListable: true };

        return new BookableListing(
            listing.Id,
            listing.HostId,
            isBookable,
            listing.Capacity,
            listing.NightlyRate,
            listing.AcceptedSpecies,
            listing.AcceptsSmall,
            listing.AcceptsMedium,
            listing.AcceptsLarge,
            listing.AvailableWeekdays);
    }

    public async Task<IReadOnlyDictionary<Guid, BookingListingSummary>> GetSummariesAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(listingIds);

        var listings = await listingDirectory.GetByIdsAsync(listingIds, cancellationToken).ConfigureAwait(false);

        return listings.ToDictionary(
            l => l.Id,
            l => new BookingListingSummary(l.Id, l.Title, l.Neighborhood, l.City, l.State));
    }

    public async Task<IReadOnlyDictionary<Guid, BookingAddress>> GetAddressesAsync(
        IReadOnlyCollection<Guid> listingIds,
        CancellationToken cancellationToken)
    {
        var addresses = await listingDirectory.GetAddressesAsync(listingIds, cancellationToken).ConfigureAwait(false);

        return addresses.ToDictionary(
            pair => pair.Key,
            pair => new BookingAddress(
                pair.Value.ZipCode,
                pair.Value.Street,
                pair.Value.Number,
                pair.Value.Complement,
                pair.Value.Neighborhood,
                pair.Value.City,
                pair.Value.State));
    }
}
