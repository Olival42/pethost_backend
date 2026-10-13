using PetHost.Modules.Owners.Application.Abstractions;
using PetHost.Shared.Contracts.Bookings;

namespace PetHost.Modules.Owners.Infrastructure.Bookings;

/// <summary>Adaptador da porta <see cref="IOwnerBookings"/> sobre o contrato público <see cref="IBookingDirectory"/>.</summary>
internal sealed class OwnerBookings(IBookingDirectory bookingDirectory) : IOwnerBookings
{
    public Task<bool> HasOpenBookingAsync(Guid ownerId, CancellationToken cancellationToken) =>
        bookingDirectory.HasOpenBookingForOwnerAsync(ownerId, cancellationToken);
}
