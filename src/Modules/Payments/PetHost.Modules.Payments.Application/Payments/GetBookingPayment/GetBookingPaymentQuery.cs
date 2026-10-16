using PetHost.Modules.Payments.Application.Abstractions;
using PetHost.Modules.Payments.Application.Payments.Responses;
using PetHost.Modules.Payments.Domain.Errors;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Payments.Application.Payments.GetBookingPayment;

/// <summary>
/// O pagamento de uma reserva: as parcelas e a situação de cada uma; para o tutor, as formas de
/// parcelar enquanto pode escolher; para o anfitrião, o repasse.
/// </summary>
/// <param name="UserId">Vem do access token.</param>
/// <param name="Role">Papel do token: <c>owner</c>, <c>host</c> ou <c>admin</c>.</param>
/// <param name="BookingId">Vem da rota.</param>
public sealed record GetBookingPaymentQuery(Guid UserId, string Role, Guid BookingId) : IQuery<PaymentResponse>;

/// <summary>
/// Só o tutor que pediu, o anfitrião do cantinho e o admin; os outros, <c>404 PAYMENT_BOOKING_NOT_FOUND</c>.
/// Reserva aceita sem plano ainda: <c>status = not_started</c> com as opções de parcelamento.
/// Outra reserva sem pagamento: <c>404 PAYMENT_NOT_FOUND</c>.
/// </summary>
public sealed class GetBookingPaymentQueryHandler(
    IPaymentRepository paymentRepository,
    IPaymentBookings bookings,
    IPaymentParties parties,
    TimeProvider timeProvider) : IQueryHandler<GetBookingPaymentQuery, PaymentResponse>
{
    public async Task<Result<PaymentResponse>> HandleAsync(GetBookingPaymentQuery query, CancellationToken cancellationToken)
    {
        var access = await PaymentAccess
            .LoadAsync(bookings, parties, query.UserId, query.Role, query.BookingId, cancellationToken)
            .ConfigureAwait(false);

        if (access.IsFailure)
            return Result<PaymentResponse>.FromFailure(access);

        var (booking, viewer) = access.Value!;

        var payment = await paymentRepository.GetByBookingIdAsync(booking.Id, cancellationToken).ConfigureAwait(false);
        if (payment is null && booking.Stage != BookingStage.AwaitingPayment)
            return Result<PaymentResponse>.Failure(PaymentsErrors.NotFound(booking.Id));

        return Result<PaymentResponse>.Success(PaymentResponseMapper.ToResponse(payment, booking, viewer, timeProvider.GetUtcNow()));
    }
}
