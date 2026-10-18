using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Application.Bookings.Responses;
using PetHost.Modules.Bookings.Domain.Bookings;

namespace PetHost.Modules.Bookings.Application.Bookings;

/// <summary>
/// Quem recebe cada aviso da reserva. Roda depois que a mudança foi gravada; o e-mail vai pela
/// caixa de saída, sem segurar o request.
/// </summary>
/// <remarks>
/// O pedido avisa só o anfitrião ("pedido novo", com o prazo para responder). O aceite avisa só
/// o tutor ("aceita", com o prazo para pagar). Quando a 1ª parcela é paga, os
/// dois recebem "confirmada" — só aí vão o endereço exato e os telefones.
/// </remarks>
public sealed class BookingNotifications(
    IBookingParties bookingParties,
    IBookingListings bookingListings,
    IBookingNotifier notifier)
{
    /// <summary>Pedido novo: aviso ao anfitrião, com o prazo para responder.</summary>
    public async Task RequestedAsync(Booking booking, BookingResponse response, CancellationToken cancellationToken)
    {
        var (owner, host) = await PartiesAsync(booking, cancellationToken).ConfigureAwait(false);

        if (host is not null)
            await notifier.NotifyRequestedAsync(Notice(host, BookingParty.Host, booking, response, owner), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Aceita: aviso ao tutor, com o prazo para pagar a 1ª parcela.</summary>
    public async Task AcceptedAsync(Booking booking, BookingResponse response, CancellationToken cancellationToken)
    {
        var (owner, host) = await PartiesAsync(booking, cancellationToken).ConfigureAwait(false);

        if (owner is not null)
            await notifier.NotifyAcceptedAsync(Notice(owner, BookingParty.Owner, booking, response, host), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Confirmada (1ª parcela paga): aviso ao tutor, com o endereço exato e o telefone do
    /// anfitrião, e ao anfitrião, com o telefone do tutor.
    /// </summary>
    public async Task ConfirmedAsync(Booking booking, BookingResponse response, CancellationToken cancellationToken)
    {
        var (owner, host) = await PartiesAsync(booking, cancellationToken).ConfigureAwait(false);

        if (owner is not null)
        {
            var address = await OwnerAddressAsync(booking, cancellationToken).ConfigureAwait(false);
            await notifier.NotifyConfirmedAsync(Notice(owner, BookingParty.Owner, booking, response, host) with { Address = address }, cancellationToken).ConfigureAwait(false);
        }

        if (host is not null)
            await notifier.NotifyConfirmedAsync(Notice(host, BookingParty.Host, booking, response, owner), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Recusada: aviso ao tutor.</summary>
    public async Task DeclinedAsync(Booking booking, BookingResponse response, CancellationToken cancellationToken)
    {
        var (owner, host) = await PartiesAsync(booking, cancellationToken).ConfigureAwait(false);

        if (owner is not null)
            await notifier.NotifyDeclinedAsync(Notice(owner, BookingParty.Owner, booking, response, host), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Cancelada: aviso aos dois lados, quem cancelou e quem foi avisado.</summary>
    public async Task CancelledAsync(Booking booking, BookingResponse response, CancellationToken cancellationToken)
    {
        var (owner, host) = await PartiesAsync(booking, cancellationToken).ConfigureAwait(false);

        if (owner is not null)
            await notifier.NotifyCancelledAsync(Notice(owner, BookingParty.Owner, booking, response, host), cancellationToken).ConfigureAwait(false);

        if (host is not null)
            await notifier.NotifyCancelledAsync(Notice(host, BookingParty.Host, booking, response, owner), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Véspera do check-in: lembrete aos dois lados (o tutor com o endereço exato).</summary>
    public async Task CheckInReminderAsync(Booking booking, BookingResponse response, CancellationToken cancellationToken)
    {
        var (owner, host) = await PartiesAsync(booking, cancellationToken).ConfigureAwait(false);

        if (owner is not null)
        {
            var address = await OwnerAddressAsync(booking, cancellationToken).ConfigureAwait(false);
            await notifier.NotifyCheckInReminderAsync(Notice(owner, BookingParty.Owner, booking, response, host) with { Address = address }, cancellationToken).ConfigureAwait(false);
        }

        if (host is not null)
            await notifier.NotifyCheckInReminderAsync(Notice(host, BookingParty.Host, booking, response, owner), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Dia de saída sem check-out: lembrete ao anfitrião.</summary>
    public async Task CheckOutReminderAsync(Booking booking, BookingResponse response, CancellationToken cancellationToken)
    {
        var (owner, host) = await PartiesAsync(booking, cancellationToken).ConfigureAwait(false);

        if (host is not null)
            await notifier.NotifyCheckOutReminderAsync(Notice(host, BookingParty.Host, booking, response, owner), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Pedido parado perto do fim do prazo: lembrete ao anfitrião.</summary>
    public async Task ResponseReminderAsync(Booking booking, BookingResponse response, CancellationToken cancellationToken)
    {
        var (owner, host) = await PartiesAsync(booking, cancellationToken).ConfigureAwait(false);

        if (host is not null)
            await notifier.NotifyResponseReminderAsync(Notice(host, BookingParty.Host, booking, response, owner), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Aceite sem pagamento perto do fim do prazo: lembrete ao tutor.</summary>
    public async Task PaymentReminderAsync(Booking booking, BookingResponse response, CancellationToken cancellationToken)
    {
        var (owner, host) = await PartiesAsync(booking, cancellationToken).ConfigureAwait(false);

        if (owner is not null)
            await notifier.NotifyPaymentReminderAsync(Notice(owner, BookingParty.Owner, booking, response, host), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Check-out feito pela plataforma: aviso aos dois lados.</summary>
    public async Task AutoCheckedOutAsync(Booking booking, BookingResponse response, CancellationToken cancellationToken)
    {
        var (owner, host) = await PartiesAsync(booking, cancellationToken).ConfigureAwait(false);

        if (owner is not null)
            await notifier.NotifyAutoCheckedOutAsync(Notice(owner, BookingParty.Owner, booking, response, host), cancellationToken).ConfigureAwait(false);

        if (host is not null)
            await notifier.NotifyAutoCheckedOutAsync(Notice(host, BookingParty.Host, booking, response, owner), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>O endereço exato só vai para o tutor de reserva que libera contato (escopo, regra 7).</summary>
    private async Task<BookingAddress?> OwnerAddressAsync(Booking booking, CancellationToken cancellationToken)
    {
        if (!booking.RevealsContact)
            return null;

        var addresses = await bookingListings.GetAddressesAsync([booking.ListingId], cancellationToken).ConfigureAwait(false);

        return addresses.GetValueOrDefault(booking.ListingId);
    }

    private async Task<(PartyInfo? Owner, PartyInfo? Host)> PartiesAsync(Booking booking, CancellationToken cancellationToken)
    {
        var owners = await bookingParties.GetOwnersAsync([booking.OwnerId], cancellationToken).ConfigureAwait(false);
        var hosts = await bookingParties.GetHostsAsync([booking.HostId], cancellationToken).ConfigureAwait(false);

        return (owners.GetValueOrDefault(booking.OwnerId), hosts.GetValueOrDefault(booking.HostId));
    }

    /// <summary>O telefone do outro lado só vai junto quando a reserva já libera contato.</summary>
    private static BookingNotice Notice(PartyInfo to, BookingParty recipient, Booking booking, BookingResponse response, PartyInfo? other) =>
        new(to.Email, to.Name, recipient, response, other?.Name, booking.RevealsContact ? other?.Phone : null);
}
