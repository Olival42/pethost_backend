using Microsoft.Extensions.Logging;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Shared.Contracts.Payments;

namespace PetHost.Modules.Bookings.Infrastructure.Adapters;

/// <summary>
/// Adaptador da porta <see cref="IBookingPayments"/> sobre os contratos públicos do Payments
/// (<see cref="IPaymentDirectory"/> e <see cref="IBookingPaymentsSync"/>).
/// </summary>
/// <remarks>
/// O aviso de mudança roda depois que a reserva foi gravada: uma falha no pagamento (Stripe fora do
/// ar, conflito) não pode desfazer o cancelamento nem o check-out. Vai para o log, e a manutenção
/// periódica do Payments relê a reserva e completa o que faltou.
/// </remarks>
internal sealed partial class BookingPayments(
    IPaymentDirectory paymentDirectory,
    IBookingPaymentsSync paymentsSync,
    ILogger<BookingPayments> logger) : IBookingPayments
{
    public Task<bool> HasOverduePaymentAsync(Guid ownerId, CancellationToken cancellationToken) =>
        paymentDirectory.HasOverdueInstallmentAsync(ownerId, cancellationToken);

    public async Task BookingChangedAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        try
        {
            await paymentsSync.SyncBookingAsync(bookingId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // O aviso ao pagamento nunca derruba a mudança já gravada: log e a manutenção reconcilia.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogSyncFailed(logger, exception, bookingId);
        }
    }

    [LoggerMessage(EventId = 8010, Level = LogLevel.Error, Message = "Payments could not be synced for booking {BookingId}; the payments maintenance will retry.")]
    private static partial void LogSyncFailed(ILogger logger, Exception exception, Guid bookingId);
}
