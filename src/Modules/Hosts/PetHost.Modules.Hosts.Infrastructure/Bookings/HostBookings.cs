using Microsoft.Extensions.Logging;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Shared.Contracts.Bookings;

namespace PetHost.Modules.Hosts.Infrastructure.Bookings;

/// <summary>
/// Adaptador da porta <see cref="IHostBookings"/> sobre os contratos públicos
/// <see cref="IBookingDirectory"/> e <see cref="IBookingSuspensions"/>.
/// </summary>
internal sealed partial class HostBookings(
    IBookingDirectory bookingDirectory,
    IBookingSuspensions bookingSuspensions,
    ILogger<HostBookings> logger) : IHostBookings
{
    public Task<bool> HasOpenBookingAsync(Guid hostId, CancellationToken cancellationToken) =>
        bookingDirectory.HasOpenBookingForHostAsync(hostId, cancellationToken);

    public Task<IReadOnlyList<Guid>> ListOpenBookingIdsAsync(Guid hostId, CancellationToken cancellationToken) =>
        bookingDirectory.ListOpenBookingIdsForHostAsync(hostId, cancellationToken);

    public async Task<int> CancelForSuspensionAsync(Guid hostId, CancellationToken cancellationToken)
    {
        try
        {
            return await bookingSuspensions.CancelForSuspendedHostAsync(hostId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A suspensão já foi gravada; suspender de novo cancela o que ficou.
            LogCancelFailed(logger, exception, hostId);
            return 0;
        }
    }

    [LoggerMessage(EventId = 7230, Level = LogLevel.Error, Message = "The bookings of suspended host {HostId} could not be cancelled; suspend again to retry.")]
    private static partial void LogCancelFailed(ILogger logger, Exception exception, Guid hostId);
}
