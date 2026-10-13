using PetHost.Modules.Pets.Application.Abstractions;
using PetHost.Shared.Contracts.Bookings;

namespace PetHost.Modules.Pets.Infrastructure.Bookings;

/// <summary>Adaptador da porta <see cref="IPetBookings"/> sobre o contrato público <see cref="IBookingDirectory"/>.</summary>
internal sealed class PetBookings(IBookingDirectory bookingDirectory) : IPetBookings
{
    public Task<bool> HasOpenBookingAsync(Guid petId, CancellationToken cancellationToken) =>
        bookingDirectory.HasOpenBookingForPetAsync(petId, cancellationToken);

    public Task<bool> IsBookedWithHostAsync(Guid petId, Guid hostId, CancellationToken cancellationToken) =>
        bookingDirectory.IsPetBookedWithHostAsync(petId, hostId, cancellationToken);
}
