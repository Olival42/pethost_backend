using Microsoft.Extensions.Logging;
using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Shared.Contracts.Bookings;

namespace PetHost.Modules.Owners.Infrastructure.Bookings;

/// <summary>
/// Adaptador da porta <see cref="IOwnerBookings"/> sobre os contratos públicos
/// <see cref="IBookingDirectory"/> e <see cref="IBookingSuspensions"/>.
/// </summary>
internal sealed partial class OwnerBookings(
    IBookingDirectory bookingDirectory,
    IBookingSuspensions bookingSuspensions,
    ILogger<OwnerBookings> logger) : IOwnerBookings
{
    public Task<bool> HasOpenBookingAsync(Guid ownerId, CancellationToken cancellationToken) =>
        bookingDirectory.HasOpenBookingForOwnerAsync(ownerId, cancellationToken);

    public async Task<int> CancelForSuspensionAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        try
        {
            return await bookingSuspensions.CancelForSuspendedOwnerAsync(ownerId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A suspensão já foi gravada; suspender de novo cancela o que ficou.
            LogCancelFailed(logger, exception, ownerId);
            return 0;
        }
    }

    [LoggerMessage(EventId = 3000, Level = LogLevel.Error, Message = "The bookings of suspended owner {OwnerId} could not be cancelled; suspend again to retry.")]
    private static partial void LogCancelFailed(ILogger logger, Exception exception, Guid ownerId);
}
