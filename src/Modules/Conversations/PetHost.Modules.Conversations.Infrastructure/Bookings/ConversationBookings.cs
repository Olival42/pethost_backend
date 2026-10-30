using PetHost.Modules.Conversations.Application.Abstractions;
using PetHost.Shared.Contracts.Bookings;

namespace PetHost.Modules.Conversations.Infrastructure.Bookings;

/// <summary>Adaptador da porta <see cref="IConversationBookings"/> sobre o contrato público <see cref="IBookingDirectory"/>.</summary>
internal sealed class ConversationBookings(IBookingDirectory bookingDirectory) : IConversationBookings
{
    public Task<Guid?> GetInProgressBookingIdAsync(Guid ownerId, Guid hostId, CancellationToken cancellationToken) =>
        bookingDirectory.GetInProgressBookingIdAsync(ownerId, hostId, cancellationToken);

    public Task<bool> HasOpenBookingAsync(Guid ownerId, Guid hostId, CancellationToken cancellationToken) =>
        bookingDirectory.HasOpenBookingBetweenAsync(ownerId, hostId, cancellationToken);
}
