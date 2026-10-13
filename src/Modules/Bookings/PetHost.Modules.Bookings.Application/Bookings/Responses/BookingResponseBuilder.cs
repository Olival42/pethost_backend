using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Domain.Bookings;

namespace PetHost.Modules.Bookings.Application.Bookings.Responses;

/// <summary>
/// Monta <see cref="BookingResponse"/> com o cantinho, o tutor, o anfitrião e os pets — uma
/// consulta por módulo para a lista inteira, nunca uma por reserva.
/// </summary>
/// <remarks>
/// O contato (<see cref="BookingResponse.Contact"/>) depende de quem vê (<c>viewer</c>): o tutor
/// recebe o telefone do anfitrião e o endereço exato; o anfitrião, o telefone do tutor; o admin
/// (<c>null</c>), nada. E só sai em reserva que já libera contato (<see cref="Booking.RevealsContact"/>).
/// </remarks>
public sealed class BookingResponseBuilder(
    IBookingListings bookingListings,
    IBookingParties bookingParties,
    IBookingPets bookingPets)
{
    public async Task<BookingResponse> BuildAsync(Booking booking, BookingParty? viewer, CancellationToken cancellationToken) =>
        (await BuildAsync([booking], viewer, cancellationToken).ConfigureAwait(false))[0];

    public async Task<IReadOnlyList<BookingResponse>> BuildAsync(
        IReadOnlyList<Booking> bookings,
        BookingParty? viewer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bookings);

        if (bookings.Count == 0)
            return [];

        var listings = await bookingListings
            .GetSummariesAsync([.. bookings.Select(b => b.ListingId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        var owners = await bookingParties
            .GetOwnersAsync([.. bookings.Select(b => b.OwnerId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        var hosts = await bookingParties
            .GetHostsAsync([.. bookings.Select(b => b.HostId).Distinct()], cancellationToken)
            .ConfigureAwait(false);

        var pets = (await bookingPets
                .GetAsync([.. bookings.SelectMany(b => b.Pets).Select(p => p.PetId).Distinct()], cancellationToken)
                .ConfigureAwait(false))
            .ToDictionary(p => p.Id);

        // O endereço exato só é lido quando algum tutor tem direito a ele.
        var addressIds = viewer == BookingParty.Owner
            ? bookings.Where(b => b.RevealsContact).Select(b => b.ListingId).Distinct().ToList()
            : [];

        IReadOnlyDictionary<Guid, BookingAddress> addresses = addressIds.Count == 0
            ? new Dictionary<Guid, BookingAddress>()
            : await bookingListings.GetAddressesAsync(addressIds, cancellationToken).ConfigureAwait(false);

        return [.. bookings.Select(booking =>
        {
            var owner = owners.GetValueOrDefault(booking.OwnerId);
            var host = hosts.GetValueOrDefault(booking.HostId);

            return ToResponse(
                booking,
                listings.GetValueOrDefault(booking.ListingId),
                owner,
                host,
                pets,
                ContactFor(booking, viewer, owner, host, addresses.GetValueOrDefault(booking.ListingId)));
        })];
    }

    private static BookingContactResponse? ContactFor(
        Booking booking,
        BookingParty? viewer,
        PartyInfo? owner,
        PartyInfo? host,
        BookingAddress? address) =>
        (booking.RevealsContact, viewer) switch
        {
            (true, BookingParty.Owner) => new BookingContactResponse(host?.Phone, address),
            (true, BookingParty.Host) => new BookingContactResponse(owner?.Phone, null),
            _ => null,
        };

    private static BookingResponse ToResponse(
        Booking booking,
        BookingListingSummary? listing,
        PartyInfo? owner,
        PartyInfo? host,
        Dictionary<Guid, BookablePet> pets,
        BookingContactResponse? contact) => new(
            booking.Id.Value,
            BookingStatusValues.ToWire(booking.Status),
            booking.ListingId,
            listing,
            booking.OwnerId,
            owner?.ToSummary(),
            booking.HostId,
            host?.ToSummary(),
            [.. booking.Pets.Select(p => pets.TryGetValue(p.PetId, out var pet)
                ? new BookingPetResponse(pet.Id, pet.Name, pet.Species, pet.Size)
                : new BookingPetResponse(p.PetId, null, null, null))],
            booking.Period.CheckIn,
            booking.Period.CheckOut,
            booking.Period.Nights,
            booking.PetCount,
            booking.NightlyRate,
            booking.TotalPrice,
            booking.RequestedAt,
            booking.Status == BookingStatus.Requested ? booking.ResponseDeadline : null,
            booking.RespondedAt,
            booking.ConfirmedAt,
            booking.CheckedInAt,
            booking.CheckedOutAt,
            booking.ExpiredAt,
            booking.CancelledAt,
            booking.CancelledBy is { } by ? BookingPartyValues.ToWire(by) : null,
            booking.CancellationReason,
            booking.DeclineReason,
            booking.RefundPercentage,
            contact,
            booking.CreatedAt,
            booking.UpdatedAt);
}
