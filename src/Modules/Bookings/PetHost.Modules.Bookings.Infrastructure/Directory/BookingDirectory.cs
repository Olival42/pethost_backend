using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Infrastructure.Persistence;
using PetHost.Shared.Contracts.Bookings;

namespace PetHost.Modules.Bookings.Infrastructure.Directory;

/// <summary>
/// Adaptador do contrato <see cref="IBookingDirectory"/> (§5): a porta pela qual o Pets, o
/// Listings e o Conversations consultam as reservas. Leitura direta das tabelas do schema <c>booking</c>.
/// </summary>
internal sealed class BookingDirectory(BookingsDbContext dbContext) : IBookingDirectory
{
    private static readonly BookingStatus[] Open =
        [BookingStatus.Requested, BookingStatus.Accepted, BookingStatus.Confirmed, BookingStatus.InProgress];

    private static readonly BookingStatus[] ShownToHost =
        [BookingStatus.Requested, BookingStatus.Accepted, BookingStatus.Confirmed, BookingStatus.InProgress, BookingStatus.Completed];

    private static readonly BookingStatus[] Occupying = [BookingStatus.Confirmed, BookingStatus.InProgress];

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
}
