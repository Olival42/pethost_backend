using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Bookings.Domain.Bookings;

namespace PetHost.Modules.Bookings.Infrastructure.Persistence.Repositories;

/// <summary>Implementação de <see cref="IBookingRepository"/> sobre o EF Core.</summary>
internal sealed class BookingRepository(BookingsDbContext dbContext) : IBookingRepository
{
    /// <summary>Status que ocupam vaga no cantinho.</summary>
    private static readonly BookingStatus[] Occupying = [BookingStatus.Accepted, BookingStatus.Confirmed, BookingStatus.InProgress];

    /// <summary>Status de reserva em aberto: o pet está comprometido com ela.</summary>
    private static readonly BookingStatus[] Open =
        [BookingStatus.Requested, BookingStatus.Accepted, BookingStatus.Confirmed, BookingStatus.InProgress];

    public Task<Booking?> GetByIdAsync(BookingId id, CancellationToken cancellationToken) =>
        dbContext.Bookings.FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Booking>> ListOccupyingAsync(Guid listingId, StayPeriod period, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(period);

        return await dbContext.Bookings
            .AsNoTracking()
            .Where(b => b.ListingId == listingId
                && Occupying.Contains(b.Status)
                && b.Period.CheckIn < period.CheckOut
                && period.CheckIn < b.Period.CheckOut)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Guid>> ListPetsBookedAsync(
        IReadOnlyCollection<Guid> petIds,
        StayPeriod period,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(petIds);
        ArgumentNullException.ThrowIfNull(period);

        if (petIds.Count == 0)
            return [];

        var ids = petIds.ToList();

        return await dbContext.Bookings
            .AsNoTracking()
            .Where(b => Open.Contains(b.Status)
                && b.Period.CheckIn < period.CheckOut
                && period.CheckIn < b.Period.CheckOut)
            .SelectMany(b => b.Pets)
            .Where(p => ids.Contains(p.PetId))
            .Select(p => p.PetId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<(IReadOnlyList<Booking> Items, long TotalCount)> SearchAsync(BookingSearch search, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);

        var query = dbContext.Bookings.AsNoTracking();

        if (search.OwnerId is { } ownerId)
            query = query.Where(b => b.OwnerId == ownerId);

        if (search.HostId is { } hostId)
            query = query.Where(b => b.HostId == hostId);

        if (search.ListingId is { } listingId)
            query = query.Where(b => b.ListingId == listingId);

        if (search.Statuses.Count > 0)
        {
            var statuses = search.Statuses.ToList();
            query = query.Where(b => statuses.Contains(b.Status));
        }

        var total = await query.LongCountAsync(cancellationToken).ConfigureAwait(false);
        if (total == 0)
            return ([], 0);

        var items = await query
            .OrderByDescending(b => b.Period.CheckIn)
            .ThenByDescending(b => b.RequestedAt)
            .ThenBy(b => b.Id)
            .Skip((search.Page - 1) * search.PageSize)
            .Take(search.PageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (items, total);
    }

    public async Task<IReadOnlyList<Booking>> ListOverdueAsync(
        DateTimeOffset requestedBefore,
        DateOnly requestedCheckInUntil,
        DateTimeOffset acceptedBefore,
        DateOnly today,
        int limit,
        CancellationToken cancellationToken) =>
        await dbContext.Bookings
            .Where(b => (b.Status == BookingStatus.Requested && (b.RequestedAt < requestedBefore || b.Period.CheckIn <= requestedCheckInUntil))
                || (b.Status == BookingStatus.Accepted && (b.RespondedAt < acceptedBefore || b.Period.CheckIn < today)))
            .OrderBy(b => b.RequestedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<BookingId>> ListCancellableIdsAsync(BookingParty party, Guid partyId, CancellationToken cancellationToken)
    {
        var bookings = dbContext.Bookings
            .AsNoTracking()
            .Where(b => b.Status == BookingStatus.Requested || b.Status == BookingStatus.Accepted || b.Status == BookingStatus.Confirmed);

        bookings = party switch
        {
            BookingParty.Owner => bookings.Where(b => b.OwnerId == partyId),
            BookingParty.Host => bookings.Where(b => b.HostId == partyId),
            _ => throw new ArgumentOutOfRangeException(nameof(party), party, "Only the owner or the host has bookings."),
        };

        return await bookings
            .OrderBy(b => b.Period.CheckIn)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Booking>> ListOverdueStaysAsync(DateOnly today, int limit, CancellationToken cancellationToken)
    {
        var autoCheckOutBefore = today.AddDays(-Booking.AutoCheckOutAfterDays);

        return await dbContext.Bookings
            .Where(b => b.Period.CheckOut <= today
                && (b.Status == BookingStatus.Confirmed
                    || (b.Status == BookingStatus.InProgress && (b.CheckOutRemindedAt == null || b.Period.CheckOut <= autoCheckOutBefore))))
            .OrderBy(b => b.Period.CheckOut)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Booking>> ListCheckInsToRemindAsync(DateOnly checkIn, int limit, CancellationToken cancellationToken) =>
        await dbContext.Bookings
            .Where(b => b.Status == BookingStatus.Confirmed && b.Period.CheckIn == checkIn && b.CheckInRemindedAt == null)
            .OrderBy(b => b.ConfirmedAt)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <summary>
    /// Os prazos são calculados (<see cref="Booking.ResponseDeadline"/>, <see cref="Booking.PaymentDeadline"/>),
    /// então o banco filtra por uma margem que os cobre — pedido de 20h atrás ou chegada em até 3 dias;
    /// aceite de 18h atrás ou chegada até amanhã — e o domínio decide.
    /// </summary>
    public async Task<IReadOnlyList<Booking>> ListDeadlinesToRemindAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        var utcNow = now.ToUniversalTime();
        var today = BookingClock.Today(utcNow);
        var requestedBefore = utcNow - (Booking.ResponseWindow - Booking.ResponseReminderLead);
        var acceptedBefore = utcNow - (Booking.PaymentWindow - Booking.PaymentReminderLead);
        var requestedCheckInUntil = today.AddDays(3);
        var acceptedCheckInUntil = today.AddDays(1);

        return await dbContext.Bookings
            .Where(b =>
                (b.Status == BookingStatus.Requested && b.ResponseRemindedAt == null
                    && (b.RequestedAt <= requestedBefore || b.Period.CheckIn <= requestedCheckInUntil))
                || (b.Status == BookingStatus.Accepted && b.PaymentRemindedAt == null
                    && (b.RespondedAt <= acceptedBefore || b.Period.CheckIn <= acceptedCheckInUntil)))
            .OrderBy(b => b.RequestedAt)
            .ThenBy(b => b.Id)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(Booking booking) => dbContext.Bookings.Add(booking);
}
