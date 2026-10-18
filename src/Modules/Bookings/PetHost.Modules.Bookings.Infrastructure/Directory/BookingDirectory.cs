using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Infrastructure.Persistence;
using PetHost.Shared.Contracts.Bookings;

namespace PetHost.Modules.Bookings.Infrastructure.Directory;

/// <summary>
/// Listings, o Conversations e o Payments consultam as reservas. Leitura direta das tabelas do schema <c>booking</c>.
/// </summary>
internal sealed class BookingDirectory(BookingsDbContext dbContext) : IBookingDirectory
{
    private static readonly BookingStatus[] Open =
        [BookingStatus.Requested, BookingStatus.Accepted, BookingStatus.Confirmed, BookingStatus.InProgress];

    private static readonly BookingStatus[] ShownToHost =
        [BookingStatus.Requested, BookingStatus.Accepted, BookingStatus.Confirmed, BookingStatus.InProgress, BookingStatus.Completed];

    private static readonly BookingStatus[] Occupying = [BookingStatus.Accepted, BookingStatus.Confirmed, BookingStatus.InProgress];

    public Task<bool> HasOpenBookingForPetAsync(Guid petId, CancellationToken cancellationToken) =>
        dbContext.Bookings
            .AsNoTracking()
            .AnyAsync(b => Open.Contains(b.Status) && b.Pets.Any(p => p.PetId == petId), cancellationToken);

    public Task<bool> HasOpenBookingForOwnerAsync(Guid ownerId, CancellationToken cancellationToken) =>
        dbContext.Bookings
            .AsNoTracking()
            .AnyAsync(b => b.OwnerId == ownerId && Open.Contains(b.Status), cancellationToken);

    public Task<bool> HasOpenBookingForHostAsync(Guid hostId, CancellationToken cancellationToken) =>
        dbContext.Bookings
            .AsNoTracking()
            .AnyAsync(b => b.HostId == hostId && Open.Contains(b.Status), cancellationToken);

    public async Task<Guid?> GetInProgressBookingIdAsync(Guid ownerId, Guid hostId, CancellationToken cancellationToken)
    {
        var ids = await dbContext.Bookings
            .AsNoTracking()
            .Where(b => b.OwnerId == ownerId && b.HostId == hostId && b.Status == BookingStatus.InProgress)
            .OrderByDescending(b => b.Period.CheckIn)
            .ThenByDescending(b => b.RequestedAt)
            .Select(b => b.Id)
            .Take(1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return ids.Count == 0 ? null : ids[0].Value;
    }

    public Task<bool> HasOpenBookingBetweenAsync(Guid ownerId, Guid hostId, CancellationToken cancellationToken) =>
        dbContext.Bookings
            .AsNoTracking()
            .AnyAsync(b => b.OwnerId == ownerId && b.HostId == hostId && Open.Contains(b.Status), cancellationToken);

    public Task<bool> HasOpenBookingForListingAsync(Guid listingId, CancellationToken cancellationToken) =>
        dbContext.Bookings
            .AsNoTracking()
            .AnyAsync(b => b.ListingId == listingId && Open.Contains(b.Status), cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListOpenBookingIdsForHostAsync(Guid hostId, CancellationToken cancellationToken)
    {
        var ids = await dbContext.Bookings
            .AsNoTracking()
            .Where(b => b.HostId == hostId && Open.Contains(b.Status))
            .OrderBy(b => b.Period.CheckIn)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. ids.Select(id => id.Value)];
    }

    public Task<bool> IsPetBookedWithHostAsync(Guid petId, Guid hostId, CancellationToken cancellationToken) =>
        dbContext.Bookings
            .AsNoTracking()
            .AnyAsync(b => b.HostId == hostId && ShownToHost.Contains(b.Status) && b.Pets.Any(p => p.PetId == petId), cancellationToken);

    /// <summary>
    /// Traz só as estadias que ocupam vaga e cruzam o período (poucas no volume do MVP) e soma por
    /// noite na memória.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, int>> GetPeakOccupancyAsync(
        DateOnly checkIn,
        DateOnly checkOut,
        CancellationToken cancellationToken)
    {
        var stays = await dbContext.Bookings
            .AsNoTracking()
            .Where(b => Occupying.Contains(b.Status) && b.Period.CheckIn < checkOut && checkIn < b.Period.CheckOut)
            .Select(b => new { b.ListingId, b.Period.CheckIn, b.Period.CheckOut, b.PetCount })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var nights = Enumerable.Range(0, checkOut.DayNumber - checkIn.DayNumber).Select(checkIn.AddDays).ToList();

        return stays
            .GroupBy(stay => stay.ListingId)
            .ToDictionary(
                group => group.Key,
                group => nights.Max(night => group.Where(s => s.CheckIn <= night && night < s.CheckOut).Sum(s => s.PetCount)));
    }

    public async Task<IReadOnlyList<StayingPet>> GetStayingPetsAsync(
        Guid listingId,
        DateOnly checkIn,
        DateOnly checkOut,
        CancellationToken cancellationToken)
    {
        var stays = await dbContext.Bookings
            .AsNoTracking()
            .Where(b => b.ListingId == listingId
                && Occupying.Contains(b.Status)
                && b.Period.CheckIn < checkOut
                && checkIn < b.Period.CheckOut)
            .OrderBy(b => b.Period.CheckIn)
            .ThenBy(b => b.RequestedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. stays.SelectMany(stay => stay.Pets.Select(pet => new StayingPet(stay.OwnerId, pet.PetId)))];
    }

    public async Task<IReadOnlyDictionary<Guid, HostBookingStats>> GetHostStatsAsync(
        IReadOnlyCollection<Guid> hostIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hostIds);

        if (hostIds.Count == 0)
            return new Dictionary<Guid, HostBookingStats>();

        var ids = hostIds.ToList();

        var rows = await dbContext.Bookings
            .AsNoTracking()
            .Where(b => ids.Contains(b.HostId))
            .Select(b => new { b.HostId, b.Status, b.RequestedAt, b.RespondedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(row => row.HostId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var responses = group
                        .Where(row => row.RespondedAt is not null)
                        .Select(row => (row.RespondedAt.GetValueOrDefault() - row.RequestedAt).Ticks)
                        .ToList();

                    return new HostBookingStats(
                        group.Count(row => row.Status == BookingStatus.Completed),
                        responses.Count == 0 ? null : TimeSpan.FromTicks((long)responses.Average()));
                });
    }

    public async Task<BookingPaymentView?> GetForPaymentAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var id = new BookingId(bookingId);

        var booking = await dbContext.Bookings
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return booking is null ? null : ToPaymentView(booking);
    }

    public async Task<BookingReviewView?> GetForReviewAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var id = new BookingId(bookingId);

        var booking = await dbContext.Bookings
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return booking is null ? null : ToReviewView(booking);
    }

    public async Task<IReadOnlyList<BookingReviewView>> ListCompletedForReviewAsync(
        DateTimeOffset checkedOutFrom,
        DateTimeOffset checkedOutTo,
        int limit,
        CancellationToken cancellationToken)
    {
        var bookings = await dbContext.Bookings
            .AsNoTracking()
            .Where(b => b.Status == BookingStatus.Completed
                && b.CheckedOutAt >= checkedOutFrom
                && b.CheckedOutAt <= checkedOutTo)
            .OrderBy(b => b.CheckedOutAt)
            .ThenBy(b => b.Id)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. bookings.Select(ToReviewView)];
    }

    private static BookingReviewView ToReviewView(Booking booking) => new(
        booking.Id.Value,
        booking.OwnerId,
        booking.HostId,
        booking.ListingId,
        BookingStatusValues.ToWire(booking.Status),
        booking.CheckedOutAt,
        [.. booking.Pets.Select(p => p.PetId)]);

    private static BookingPaymentView ToPaymentView(Booking booking) => new(
        booking.Id.Value,
        booking.OwnerId,
        booking.HostId,
        BookingStatusValues.ToWire(booking.Status),
        booking.TotalPrice,
        booking.Period.CheckIn,
        booking.Period.CheckOut,
        booking.PaymentDeadline,
        booking.RefundPercentage);
}
