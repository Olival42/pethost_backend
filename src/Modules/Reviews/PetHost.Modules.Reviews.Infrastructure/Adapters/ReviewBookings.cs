using PetHost.Modules.Reviews.Application.Abstractions;
using PetHost.Modules.Reviews.Domain.Reviews;
using PetHost.Shared.Contracts.Bookings;

namespace PetHost.Modules.Reviews.Infrastructure.Adapters;

/// <summary>Adaptador da porta <see cref="IReviewBookings"/> sobre o contrato público <see cref="IBookingDirectory"/>.</summary>
internal sealed class ReviewBookings(IBookingDirectory bookingDirectory) : IReviewBookings
{
    public async Task<ReviewedStay?> GetStayAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await bookingDirectory.GetForReviewAsync(bookingId, cancellationToken).ConfigureAwait(false);

        return booking is null ? null : ToStay(booking);
    }

    public async Task<IReadOnlyList<ReviewedStay>> ListCompletedAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        int limit,
        CancellationToken cancellationToken)
    {
        var bookings = await bookingDirectory.ListCompletedForReviewAsync(from, to, limit, cancellationToken).ConfigureAwait(false);

        return [.. bookings.Select(ToStay)];
    }

    private static ReviewedStay ToStay(BookingReviewView booking) => new(
        booking.Id,
        booking.OwnerId,
        booking.HostId,
        booking.ListingId,
        booking.Status == BookingPaymentView.Completed,
        booking.CheckedOutAt,
        booking.PetIds);
}
