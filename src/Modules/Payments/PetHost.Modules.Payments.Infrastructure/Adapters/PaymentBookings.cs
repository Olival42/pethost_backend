using PetHost.Modules.Payments.Application.Abstractions;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Contracts.Bookings;

namespace PetHost.Modules.Payments.Infrastructure.Adapters;

/// <summary>
/// Adaptador da porta <see cref="IPaymentBookings"/> sobre os contratos públicos do Bookings
/// (<see cref="IBookingDirectory"/> e <see cref="IBookingPaymentOutcomes"/>).
/// </summary>
internal sealed class PaymentBookings(
    IBookingDirectory bookingDirectory,
    IBookingPaymentOutcomes bookingPaymentOutcomes) : IPaymentBookings
{
    public async Task<PaymentBooking?> GetAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await bookingDirectory.GetForPaymentAsync(bookingId, cancellationToken).ConfigureAwait(false);

        return booking is null ? null : ToPaymentBooking(booking);
    }

    public Task<BookingPaymentOutcome> ConfirmPaidAsync(Guid bookingId, CancellationToken cancellationToken) =>
        bookingPaymentOutcomes.ConfirmPaidAsync(bookingId, cancellationToken);

    public Task<BookingPaymentOutcome> CancelUnpaidAsync(Guid bookingId, CancellationToken cancellationToken) =>
        bookingPaymentOutcomes.CancelUnpaidAsync(bookingId, cancellationToken);

    private static PaymentBooking ToPaymentBooking(BookingPaymentView booking) => new(
        booking.Id,
        booking.OwnerId,
        booking.HostId,
        StageOf(booking.Status),
        PayableBooking.ToCents(booking.TotalPrice),
        booking.CheckIn,
        booking.CheckOut,
        booking.PaymentDeadline,
        booking.RefundPercentage);

    /// <summary>Status da reserva no vocabulário do pagamento. Recusada ou expirada: fechada (nada a cobrar).</summary>
    internal static BookingStage StageOf(string status) => status switch
    {
        BookingPaymentView.Requested => BookingStage.Requested,
        BookingPaymentView.Accepted => BookingStage.AwaitingPayment,
        BookingPaymentView.Confirmed => BookingStage.Confirmed,
        BookingPaymentView.InProgress => BookingStage.InProgress,
        BookingPaymentView.Completed => BookingStage.Completed,
        BookingPaymentView.Cancelled => BookingStage.Cancelled,
        _ => BookingStage.Closed,
    };
}
