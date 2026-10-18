using Microsoft.EntityFrameworkCore;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Infrastructure.Persistence;
using PetHost.Shared.Contracts.Bookings;

namespace PetHost.Modules.Bookings.Infrastructure.Directory;

/// <summary>
/// Listings, o Conversations e o Payments consultam as reservas. Leitura direta das tabelas do schema <c>booking</c>.
/// O painel do anfitrião (Hosts) reaproveita o <see cref="BookingResponseBuilder"/> para o cantinho, o tutor e os pets.
/// </summary>
internal sealed class BookingDirectory(
    BookingsDbContext dbContext,
    BookingResponseBuilder responseBuilder,
    TimeProvider timeProvider) : IBookingDirectory
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

    /// <summary>
    /// Traz as reservas em aberto do anfitrião (poucas no volume do MVP), separa na memória e monta só
    /// os itens mostrados. Pedido e aceite com o prazo vencido — que a tarefa de expiração ainda não
    /// fechou — já não contam.
    /// </summary>
    public async Task<HostBookingsOverview> GetHostOverviewAsync(Guid hostId, int itemLimit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(itemLimit);

        var now = timeProvider.GetUtcNow();

        var open = await dbContext.Bookings
            .AsNoTracking()
            .Where(b => b.HostId == hostId && Open.Contains(b.Status))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var pending = open
            .Where(b => b.Status == BookingStatus.Requested && !b.IsResponseOverdue(now))
            .OrderBy(b => b.ResponseDeadline)
            .ThenBy(b => b.RequestedAt)
            .ToList();

        var awaitingPayment = open.Count(b => b.Status == BookingStatus.Accepted && !b.IsPaymentOverdue(now));

        var inProgress = open
            .Where(b => b.Status == BookingStatus.InProgress)
            .OrderBy(b => b.Period.CheckOut)
            .ThenBy(b => b.Period.CheckIn)
            .ToList();

        // Paga e com a chegada já passada sem check-in: não é mais "próxima" — o anfitrião precisa
        // marcar "Chegou" (ou o pet não veio, e a tarefa cancela no dia de saída).
        var today = BookingClock.Today(now);
        var confirmed = open
            .Where(b => b.Status == BookingStatus.Confirmed)
            .OrderBy(b => b.Period.CheckIn)
            .ThenBy(b => b.RequestedAt)
            .ToList();
        var overdueCheckIns = confirmed.Where(b => b.Period.CheckIn < today).ToList();
        var upcoming = confirmed.Where(b => b.Period.CheckIn >= today).ToList();

        var shown = pending.Take(itemLimit)
            .Concat(inProgress.Take(itemLimit))
            .Concat(overdueCheckIns.Take(itemLimit))
            .Concat(upcoming.Take(itemLimit))
            .ToList();

        var summaries = (await responseBuilder.BuildAsync(shown, BookingParty.Host, cancellationToken).ConfigureAwait(false))
            .ToDictionary(response => response.Id, ToHostSummary);

        return new HostBookingsOverview(
            pending.Count,
            [.. pending.Take(itemLimit).Select(b => summaries[b.Id.Value])],
            awaitingPayment,
            inProgress.Count,
            [.. inProgress.Take(itemLimit).Select(b => summaries[b.Id.Value])],
            overdueCheckIns.Count,
            [.. overdueCheckIns.Take(itemLimit).Select(b => summaries[b.Id.Value])],
            upcoming.Count,
            [.. upcoming.Take(itemLimit).Select(b => summaries[b.Id.Value])]);
    }

    /// <summary>O Npgsql só grava <c>timestamptz</c> em UTC: o intervalo (que pode vir em Brasília) é convertido antes.</summary>
    public Task<int> CountCompletedStaysAsync(
        Guid hostId,
        DateTimeOffset checkedOutFrom,
        DateTimeOffset checkedOutTo,
        CancellationToken cancellationToken)
    {
        var from = checkedOutFrom.ToUniversalTime();
        var to = checkedOutTo.ToUniversalTime();

        return dbContext.Bookings
            .AsNoTracking()
            .CountAsync(b => b.HostId == hostId
                && b.Status == BookingStatus.Completed
                && b.CheckedOutAt >= from
                && b.CheckedOutAt < to, cancellationToken);
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

    private static HostBookingSummary ToHostSummary(BookingResponse response) => new(
        response.Id,
        response.ListingId,
        response.Listing?.Title,
        response.Listing?.Neighborhood,
        response.OwnerId,
        response.Owner?.Name,
        response.Owner?.AvatarUrl,
        response.OwnerRating is { } rating ? new HostBookingOwnerRating(rating.Average, rating.Count) : null,
        [.. response.Pets.Select(p => new HostBookingPet(p.Id, p.Name, p.Species, p.Size))],
        response.CheckIn,
        response.CheckOut,
        response.Nights,
        response.PetCount,
        response.TotalPrice,
        response.RequestedAt,
        response.ResponseDeadline,
        response.CheckedInAt);

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
