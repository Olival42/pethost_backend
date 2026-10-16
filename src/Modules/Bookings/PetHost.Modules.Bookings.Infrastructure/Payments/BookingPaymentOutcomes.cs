using PetHost.Modules.Bookings.Application.Bookings.CancelUnpaidBooking;
using PetHost.Modules.Bookings.Application.Bookings.ConfirmBookingPayment;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Contracts.Bookings;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Infrastructure.Payments;

/// <summary>
/// Adaptador do contrato <see cref="IBookingPaymentOutcomes"/> (§5): o módulo de pagamento
/// confirma a reserva paga ou cancela a que ficou sem pagar. Cada chamada vira o caso de uso do
/// Bookings, que guarda a regra, grava, registra na trilha e manda os e-mails.
/// </summary>
internal sealed class BookingPaymentOutcomes(
    ICommandHandler<ConfirmBookingPaymentCommand, bool> confirmBookingPayment,
    ICommandHandler<CancelUnpaidBookingCommand, bool> cancelUnpaidBooking) : IBookingPaymentOutcomes
{
    public async Task<BookingPaymentOutcome> ConfirmPaidAsync(Guid bookingId, CancellationToken cancellationToken) =>
        ToOutcome(await confirmBookingPayment.HandleAsync(new ConfirmBookingPaymentCommand(bookingId), cancellationToken).ConfigureAwait(false));

    public async Task<BookingPaymentOutcome> CancelUnpaidAsync(Guid bookingId, CancellationToken cancellationToken) =>
        ToOutcome(await cancelUnpaidBooking.HandleAsync(new CancelUnpaidBookingCommand(bookingId), cancellationToken).ConfigureAwait(false));

    /// <summary>Mudou agora, já estava assim, conflito de gravação (tente de novo) ou não se aplica.</summary>
    private static BookingPaymentOutcome ToOutcome(Result<bool> result) => result switch
    {
        { IsSuccess: true, Value: true } => BookingPaymentOutcome.Done,
        { IsSuccess: true } => BookingPaymentOutcome.AlreadyDone,
        _ when result.FirstError?.Code == BookingsErrors.Conflict.Code => BookingPaymentOutcome.Conflict,
        _ => BookingPaymentOutcome.NotApplicable,
    };
}
