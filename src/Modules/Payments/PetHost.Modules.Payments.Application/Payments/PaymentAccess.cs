using PetHost.Modules.Payments.Application.Abstractions;
using PetHost.Modules.Payments.Domain.Errors;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Payments.Application.Payments;

/// <summary>
/// Quem vê o pagamento de qual reserva: o tutor que pediu, o anfitrião do cantinho e o admin.
/// Reserva de outra pessoa responde igual a inexistente, <c>404 PAYMENT_BOOKING_NOT_FOUND</c> (§8).
/// </summary>
public static class PaymentAccess
{
    /// <summary>
    /// A reserva e o lado de quem pede, pelo papel do token. Conta sem o perfil do papel:
    /// <c>404 PAYMENT_OWNER_NOT_FOUND</c> / <c>PAYMENT_HOST_NOT_FOUND</c>.
    /// </summary>
    public static async Task<Result<ViewedBooking>> LoadAsync(
        IPaymentBookings bookings,
        IPaymentParties parties,
        Guid userId,
        string role,
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bookings);
        ArgumentNullException.ThrowIfNull(parties);

        var viewer = role switch
        {
            Roles.Admin => PaymentViewer.Admin,
            Roles.Host => PaymentViewer.Host,
            _ => PaymentViewer.Owner,
        };

        Guid? partyId = null;
        if (viewer == PaymentViewer.Owner)
        {
            partyId = await parties.FindOwnerIdAsync(userId, cancellationToken).ConfigureAwait(false);
            if (partyId is null)
                return Result<ViewedBooking>.Failure(PaymentsErrors.OwnerNotFound);
        }
        else if (viewer == PaymentViewer.Host)
        {
            partyId = await parties.FindHostIdAsync(userId, cancellationToken).ConfigureAwait(false);
            if (partyId is null)
                return Result<ViewedBooking>.Failure(PaymentsErrors.HostNotFound);
        }

        var booking = await bookings.GetAsync(bookingId, cancellationToken).ConfigureAwait(false);

        var visible = booking is not null && viewer switch
        {
            PaymentViewer.Owner => booking.OwnerId == partyId,
            PaymentViewer.Host => booking.HostId == partyId,
            _ => true,
        };

        return visible
            ? Result<ViewedBooking>.Success(new ViewedBooking(booking!, viewer))
            : Result<ViewedBooking>.Failure(PaymentsErrors.BookingNotFound(bookingId));
    }
}

/// <summary>Quem está vendo o pagamento: muda o que aparece (opções de parcelamento, repasse, ids do Stripe).</summary>
public enum PaymentViewer
{
    /// <summary>O tutor que paga.</summary>
    Owner,

    /// <summary>O anfitrião que recebe.</summary>
    Host,

    /// <summary>O admin (vê os ids do Stripe, para conferir no Dashboard).</summary>
    Admin,
}

/// <summary>Uma reserva e quem a vê, já conferido.</summary>
public sealed record ViewedBooking(PaymentBooking Booking, PaymentViewer Viewer);
