using Microsoft.Extensions.Logging;
using PetHost.Modules.Payments.Application.Payments.ReconcileBookingPayment;
using PetHost.Shared.Contracts.Payments;
using PetHost.Shared.Kernel.Messaging;
using PetHost.Shared.Kernel.Primitives;

namespace PetHost.Modules.Payments.Infrastructure.Contracts;

/// <summary>
/// Adaptador do contrato <see cref="IBookingPaymentsSync"/> (§5): o Bookings avisa que a reserva
/// mudou e o pagamento reconcilia (<see cref="ReconcileBookingPaymentCommand"/>).
/// </summary>
/// <remarks>Falha (conflito, Stripe fora do ar) vai para o log: a manutenção periódica completa depois.</remarks>
internal sealed partial class BookingPaymentsSync(
    ICommandHandler<ReconcileBookingPaymentCommand, Unit> reconcileBookingPayment,
    ILogger<BookingPaymentsSync> logger) : IBookingPaymentsSync
{
    public async Task SyncBookingAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var result = await reconcileBookingPayment.HandleAsync(new ReconcileBookingPaymentCommand(bookingId), cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
            LogSyncFailed(logger, bookingId, result.FirstError?.Code ?? "unknown");
    }

    [LoggerMessage(EventId = 9300, Level = LogLevel.Warning, Message = "Payment of booking {BookingId} could not be reconciled now ({ErrorCode}); the maintenance will retry.")]
    private static partial void LogSyncFailed(ILogger logger, Guid bookingId, string errorCode);
}
