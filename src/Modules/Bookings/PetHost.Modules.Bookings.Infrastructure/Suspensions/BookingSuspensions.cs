using Microsoft.Extensions.Logging;
using PetHost.Modules.Bookings.Application.Bookings.CancelBookingsForSuspension;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Shared.Contracts.Bookings;
using PetHost.Shared.Kernel.Messaging;

namespace PetHost.Modules.Bookings.Infrastructure.Suspensions;

/// <summary>
/// Adaptador do contrato <see cref="IBookingSuspensions"/> (§5): o Owners ou o Hosts avisam que o
/// admin suspendeu um dos lados, e o caso de uso do Bookings cancela as reservas que ainda não
/// começaram. Reserva que não pôde ser gravada (conflito repetido) vai para o log.
/// </summary>
internal sealed partial class BookingSuspensions(
    ICommandHandler<CancelBookingsForSuspensionCommand, CancelBookingsForSuspensionResponse> cancelBookings,
    ILogger<BookingSuspensions> logger) : IBookingSuspensions
{
    public Task<int> CancelForSuspendedOwnerAsync(Guid ownerId, CancellationToken cancellationToken) =>
        CancelAsync(BookingParty.Owner, ownerId, cancellationToken);

    public Task<int> CancelForSuspendedHostAsync(Guid hostId, CancellationToken cancellationToken) =>
        CancelAsync(BookingParty.Host, hostId, cancellationToken);

    private async Task<int> CancelAsync(BookingParty suspended, Guid partyId, CancellationToken cancellationToken)
    {
        var result = await cancelBookings
            .HandleAsync(new CancelBookingsForSuspensionCommand(suspended, partyId), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
            return 0;

        foreach (var bookingId in result.Value!.NotCancelled)
            LogNotCancelled(logger, bookingId);

        return result.Value.Cancelled;
    }

    [LoggerMessage(EventId = 8020, Level = LogLevel.Warning, Message = "Booking {BookingId} was not cancelled after the suspension (changed at the same time); suspending again retries it.")]
    private static partial void LogNotCancelled(ILogger logger, Guid bookingId);
}
