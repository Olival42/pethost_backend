using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Shared.Contracts.Bookings;

namespace PetHost.Modules.Hosts.Infrastructure.Bookings;

/// <summary>Adaptador da porta <see cref="IHostBookings"/> sobre o contrato público <see cref="IBookingDirectory"/>.</summary>
internal sealed class HostBookings(IBookingDirectory bookingDirectory) : IHostBookings
{
    public Task<bool> HasOpenBookingAsync(Guid hostId, CancellationToken cancellationToken) =>
        bookingDirectory.HasOpenBookingForHostAsync(hostId, cancellationToken);
}
