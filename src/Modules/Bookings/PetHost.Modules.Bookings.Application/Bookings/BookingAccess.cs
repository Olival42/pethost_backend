using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Modules.Bookings.Domain.Errors;
using PetHost.Shared.Contracts.Authorization;
using PetHost.Shared.Kernel.Results;

namespace PetHost.Modules.Bookings.Application.Bookings;

/// <summary>
/// Quem pode ver e mexer em qual reserva: o tutor que pediu e o anfitrião dono do cantinho.
/// Reserva de outra pessoa responde igual a inexistente, <c>404 BOOKING_NOT_FOUND</c> (§8).
/// </summary>
public static class BookingAccess
{
    /// <summary>
    /// O tutor da conta logada. Sem perfil de tutor: <c>404 BOOKING_OWNER_NOT_FOUND</c>. Com
    /// <paramref name="requireAvailable"/>, inativo ou suspenso: <c>403 BOOKING_OWNER_DEACTIVATED</c>.
    /// </summary>
    public static async Task<Result<PartyProfile>> ResolveOwnerAsync(
        IBookingParties bookingParties,
        Guid userId,
        bool requireAvailable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bookingParties);

        var owner = await bookingParties.FindOwnerByAccountAsync(userId, cancellationToken).ConfigureAwait(false);
        if (owner is null)
            return Result<PartyProfile>.Failure(BookingsErrors.OwnerNotFound);

        if (requireAvailable && !owner.IsAvailable)
            return Result<PartyProfile>.Failure(BookingsErrors.OwnerDeactivated);

        return Result<PartyProfile>.Success(owner);
    }

    /// <summary>
    /// O anfitrião da conta logada. Sem perfil de anfitrião: <c>404 BOOKING_HOST_NOT_FOUND</c>.
    /// Com <paramref name="requireAvailable"/>, inativo ou suspenso: <c>403 BOOKING_HOST_DEACTIVATED</c>.
    /// </summary>
    public static async Task<Result<PartyProfile>> ResolveHostAsync(
        IBookingParties bookingParties,
        Guid userId,
        bool requireAvailable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bookingParties);

        var host = await bookingParties.FindHostByAccountAsync(userId, cancellationToken).ConfigureAwait(false);
        if (host is null)
            return Result<PartyProfile>.Failure(BookingsErrors.HostNotFound);

        if (requireAvailable && !host.IsAvailable)
            return Result<PartyProfile>.Failure(BookingsErrors.HostDeactivated);

        return Result<PartyProfile>.Success(host);
    }

    /// <summary>
    /// A reserva <paramref name="bookingId"/>, rastreada, se for de um cantinho do anfitrião da
    /// conta logada (ativo e sem suspensão: é ele quem age sobre a estadia).
    /// </summary>
    public static async Task<Result<Booking>> LoadForHostAsync(
        IBookingRepository bookingRepository,
        IBookingParties bookingParties,
        Guid userId,
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bookingRepository);

        var host = await ResolveHostAsync(bookingParties, userId, requireAvailable: true, cancellationToken).ConfigureAwait(false);
        if (host.IsFailure)
            return Result<Booking>.FromFailure(host);

        var id = new BookingId(bookingId);
        var booking = await bookingRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (booking is null || !booking.IsHostedBy(host.Value!.Id))
            return Result<Booking>.Failure(BookingsErrors.NotFound(id));

        return Result<Booking>.Success(booking);
    }

    /// <summary>
    /// A reserva <paramref name="bookingId"/>, rastreada, e o lado de quem pede: o tutor que pediu
    /// (papel <c>owner</c>) ou o anfitrião do cantinho (papel <c>host</c>). Não exige perfil ativo:
    /// quem inativou a conta ainda pode cancelar o que estava marcado.
    /// </summary>
    public static async Task<Result<PartyBooking>> LoadForPartyAsync(
        IBookingRepository bookingRepository,
        IBookingParties bookingParties,
        Guid userId,
        string role,
        Guid bookingId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bookingRepository);

        var side = role == Roles.Host ? BookingParty.Host : BookingParty.Owner;

        var party = side == BookingParty.Host
            ? await ResolveHostAsync(bookingParties, userId, requireAvailable: false, cancellationToken).ConfigureAwait(false)
            : await ResolveOwnerAsync(bookingParties, userId, requireAvailable: false, cancellationToken).ConfigureAwait(false);

        if (party.IsFailure)
            return Result<PartyBooking>.FromFailure(party);

        var id = new BookingId(bookingId);
        var booking = await bookingRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        var isParty = booking is not null && (side == BookingParty.Host
            ? booking.IsHostedBy(party.Value!.Id)
            : booking.IsRequestedBy(party.Value!.Id));

        if (!isParty)
            return Result<PartyBooking>.Failure(BookingsErrors.NotFound(id));

        return Result<PartyBooking>.Success(new PartyBooking(booking!, side));
    }
}

/// <summary>Uma reserva e o lado de quem pede, já conferido.</summary>
public sealed record PartyBooking(Booking Booking, BookingParty Side);
