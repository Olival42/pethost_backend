using PetHost.Modules.Listings.Application.Abstractions;
using PetHost.Shared.Contracts.Bookings;

namespace PetHost.Modules.Listings.Infrastructure.Bookings;

/// <summary>Adaptador da porta <see cref="IListingBookings"/> sobre o contrato público <see cref="IBookingDirectory"/>.</summary>
internal sealed class ListingBookings(IBookingDirectory bookingDirectory) : IListingBookings
{
    public Task<IReadOnlyDictionary<Guid, int>> GetPeakOccupancyAsync(DateOnly checkIn, DateOnly checkOut, CancellationToken cancellationToken) =>
        bookingDirectory.GetPeakOccupancyAsync(checkIn, checkOut, cancellationToken);

    public async Task<IReadOnlyList<ListingGuest>> GetGuestsAsync(Guid listingId, DateOnly checkIn, DateOnly checkOut, CancellationToken cancellationToken)
    {
        var stays = await bookingDirectory.GetStayingPetsAsync(listingId, checkIn, checkOut, cancellationToken).ConfigureAwait(false);

        return [.. stays.Select(stay => new ListingGuest(stay.OwnerId, stay.PetId))];
    }

    public Task<bool> HasOpenBookingAsync(Guid listingId, CancellationToken cancellationToken) =>
        bookingDirectory.HasOpenBookingForListingAsync(listingId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, HostTrackRecord>> GetTrackRecordsAsync(
        IReadOnlyCollection<Guid> hostIds,
        CancellationToken cancellationToken)
    {
        var stats = await bookingDirectory.GetHostStatsAsync(hostIds, cancellationToken).ConfigureAwait(false);

        return stats.ToDictionary(
            pair => pair.Key,
            pair => new HostTrackRecord(pair.Value.CompletedStays, pair.Value.AverageResponseTime));
    }
}
