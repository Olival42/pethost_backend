using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Contracts.Bookings;

namespace PetHost.Modules.Payments.Application.Abstractions;

/// <summary>
/// Porta para as reservas (módulo Bookings). §5: a Application declara a interface; a
/// Infrastructure implementa via <c>Shared.Contracts</c>.
/// </summary>
public interface IPaymentBookings
{
    /// <summary>A reserva, ou <c>null</c> se ela não existe.</summary>
    Task<PaymentBooking?> GetAsync(Guid bookingId, CancellationToken cancellationToken);

    /// <summary>A 1ª parcela foi paga: confirma a reserva aceita.</summary>
    Task<BookingPaymentOutcome> ConfirmPaidAsync(Guid bookingId, CancellationToken cancellationToken);

    /// <summary>Parcela vencida antes do check-in: a plataforma cancela a reserva, com 100% de reembolso.</summary>
    Task<BookingPaymentOutcome> CancelUnpaidAsync(Guid bookingId, CancellationToken cancellationToken);
}

/// <summary>A reserva no vocabulário do pagamento.</summary>
/// <param name="OwnerId">Tutor que paga.</param>
/// <param name="HostId">Anfitrião que recebe.</param>
/// <param name="AmountCents">Valor total, em centavos.</param>
/// <param name="PaymentDeadline">Prazo da 1ª parcela, enquanto a reserva está aceita.</param>
/// <param name="RefundPercentage">Reembolso da reserva paga e cancelada.</param>
public sealed record PaymentBooking(
    Guid Id,
    Guid OwnerId,
    Guid HostId,
    BookingStage Stage,
    long AmountCents,
    DateOnly CheckIn,
    DateOnly CheckOut,
    DateTimeOffset? PaymentDeadline,
    int? RefundPercentage)
{
    /// <summary>O pet ainda não chegou: parcela vencida cancela a reserva (depois, só fica em atraso).</summary>
    public bool IsBeforeCheckIn => Stage is BookingStage.AwaitingPayment or BookingStage.Confirmed;
}
