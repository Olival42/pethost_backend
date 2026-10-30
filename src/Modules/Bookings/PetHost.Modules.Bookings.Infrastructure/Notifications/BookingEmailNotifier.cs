using System.Globalization;
using System.Net;
using PetHost.Modules.Bookings.Application.Abstractions;
using PetHost.Modules.Bookings.Domain.Bookings;
using PetHost.Shared.Infrastructure.Email;

namespace PetHost.Modules.Bookings.Infrastructure.Notifications;

/// <summary>
/// E-mails da reserva: pedido novo (ao anfitrião, com o prazo para responder), aceita (com o
/// prazo para pagar) e recusada (ao tutor), confirmada e cancelada (aos dois), véspera da estadia
/// (aos dois), lembrete do check-out
/// (ao anfitrião) e check-out feito pela plataforma (aos dois). Vão pela caixa de saída (<see cref="IEmailOutbox"/>), em segundo plano.
/// </summary>
internal sealed class BookingEmailNotifier(IEmailOutbox outbox) : IBookingNotifier
{
    private const string RequestedSubject = "PetHost — você recebeu um pedido de reserva";
    private const string AcceptedSubject = "PetHost — sua reserva foi aceita!";
    private const string DeclinedSubject = "PetHost — sua reserva não foi aceita";
    private const string ConfirmedSubject = "PetHost — você tem uma reserva confirmada";
    private const string OwnerConfirmedSubject = "PetHost — pagamento confirmado, reserva garantida!";
    private const string CancelledSubject = "PetHost — reserva cancelada";
    private const string CheckInReminderSubject = "PetHost — a estadia começa amanhã";
    private const string CheckOutReminderSubject = "PetHost — o pet já voltou pra casa? Marque o check-out";
    private const string AutoCheckedOutSubject = "PetHost — estadia concluída";
    private const string ResponseReminderSubject = "PetHost — um pedido ainda espera a sua resposta";
    private const string PaymentReminderSubject = "PetHost — falta pagar para garantir a reserva";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Horário de Brasília (UTC−3), para mostrar prazos.</summary>
    private static readonly TimeSpan Brasilia = BookingClock.LocalOffset;

    public Task NotifyRequestedAsync(BookingNotice notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);

        return SendAsync(
            notice,
            RequestedSubject,
            [
                $"Olá, {notice.Name}.",
                $"{notice.OtherPartyName ?? "Um tutor"} quer deixar o pet com você!",
                .. Summary(notice),
                .. ResponseDeadline(notice),
                "Abra o pedido no app para ver a ficha completa do pet e aceitar ou recusar. O telefone e o endereço do tutor aparecem depois que ele pagar.",
            ],
            cancellationToken);
    }

    public Task NotifyAcceptedAsync(BookingNotice notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);

        return SendAsync(
            notice,
            AcceptedSubject,
            [
                $"Olá, {notice.Name}.",
                $"{notice.OtherPartyName ?? "O anfitrião"} aceitou o seu pedido!",
                .. Summary(notice),
                .. PaymentDeadline(notice),
                "Pague pelo app, com Pix ou cartão, à vista ou em até 6 parcelas. A reserva fica confirmada assim que a 1ª parcela for paga; aí liberamos o endereço e o telefone do anfitrião.",
            ],
            cancellationToken);
    }

    public Task NotifyDeclinedAsync(BookingNotice notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);

        return SendAsync(
            notice,
            DeclinedSubject,
            [
                $"Olá, {notice.Name}.",
                $"{notice.OtherPartyName ?? "O anfitrião"} não pôde aceitar o seu pedido de estadia.",
                .. Summary(notice),
                .. Reason(notice.Booking.DeclineReason),
                "Que tal procurar outro cantinho no app? Há outros anfitriões perto de você.",
            ],
            cancellationToken);
    }

    public Task NotifyConfirmedAsync(BookingNotice notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);

        if (notice.Recipient == BookingParty.Owner)
        {
            return SendAsync(
                notice,
                OwnerConfirmedSubject,
                [
                    $"Olá, {notice.Name}.",
                    "Recebemos o seu pagamento e a reserva está confirmada.",
                    .. Summary(notice),
                    .. Contact(notice, "Telefone do anfitrião"),
                    "Leve o seu pet no dia da chegada. Se pagou parcelado, as próximas parcelas vencem a cada 30 dias e você recebe um lembrete antes de cada uma. Se precisar cancelar, faça pelo app: com 48h de antecedência o reembolso é integral; depois disso, de 50% do que foi pago.",
                ],
                cancellationToken);
        }

        return SendAsync(
            notice,
            ConfirmedSubject,
            [
                $"Olá, {notice.Name}.",
                $"A reserva de {notice.OtherPartyName ?? "um tutor"} está confirmada.",
                .. Summary(notice),
                .. Contact(notice, "Telefone do tutor"),
                "No dia da chegada, faça o check-in pelo app quando receber o pet.",
            ],
            cancellationToken);
    }

    public Task NotifyCancelledAsync(BookingNotice notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);

        var booking = notice.Booking;
        var cancelledByRecipient = booking.CancelledBy == BookingPartyValues.ToWire(notice.Recipient);
        var otherSide = notice.Recipient == BookingParty.Owner ? "O anfitrião" : "O tutor";

        // Plataforma sem motivo: parcela vencida. Pet que não chegou: texto próprio. Outro motivo: o
        // admin cancelou ou suspendeu um dos lados.
        var who = booking.CancelledBy == BookingPartyValues.Platform
            ? booking.CancellationReason switch
            {
                null or "" => "A reserva foi cancelada automaticamente porque uma parcela não foi paga no prazo.",
                Booking.NoShowReason => "A estadia não aconteceu: o check-in não foi feito até o dia de saída, então a reserva foi encerrada.",
                _ => "A reserva foi cancelada pela equipe do PetHost.",
            }
            : cancelledByRecipient ? "Você cancelou a reserva." : $"{notice.OtherPartyName ?? otherSide} cancelou a reserva.";

        return SendAsync(
            notice,
            CancelledSubject,
            [
                $"Olá, {notice.Name}.",
                who,
                .. Summary(notice),
                .. Reason(booking.CancellationReason),
                .. Refund(notice),
            ],
            cancellationToken);
    }

    public Task NotifyCheckInReminderAsync(BookingNotice notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);

        var pets = string.Join(", ", notice.Booking.Pets.Select(p => p.Name ?? "pet"));
        var tips = notice.Recipient == BookingParty.Owner
            ? new[]
            {
                $"Amanhã {pets} vai para o cantinho de {notice.OtherPartyName ?? "seu anfitrião"}.",
                "Leve a ração, os remédios (com os horários) e a coleira, e confira se a ficha do pet no app está em dia: é ela que o anfitrião vai seguir.",
            }
            : new[]
            {
                $"Amanhã você recebe {pets}, de {notice.OtherPartyName ?? "um tutor"}.",
                "Antes de chegarem, releia a ficha do pet no app (alimentação, remédios, com quem se dá bem). Quando o pet chegar, toque em \"Chegou\".",
            };

        return SendAsync(
            notice,
            CheckInReminderSubject,
            [
                $"Olá, {notice.Name}.",
                .. tips,
                .. Summary(notice),
                .. Contact(notice, notice.Recipient == BookingParty.Owner ? "Telefone do anfitrião" : "Telefone do tutor"),
            ],
            cancellationToken);
    }

    public Task NotifyCheckOutReminderAsync(BookingNotice notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);

        return SendAsync(
            notice,
            CheckOutReminderSubject,
            [
                $"Olá, {notice.Name}.",
                "Hoje é o dia de saída desta estadia e o check-out ainda não foi feito.",
                .. Summary(notice),
                $"Quando o pet voltar pra casa, toque em \"Voltou pra casa\" no app: é isso que libera o seu repasse. Se não for marcado, concluímos a estadia sozinhos {Booking.AutoCheckOutAfterDays} dias depois do dia de saída.",
            ],
            cancellationToken);
    }

    public Task NotifyResponseReminderAsync(BookingNotice notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);

        return SendAsync(
            notice,
            ResponseReminderSubject,
            [
                $"Olá, {notice.Name}.",
                $"{notice.OtherPartyName ?? "Um tutor"} ainda espera a sua resposta, e o prazo acaba em poucas horas.",
                .. Summary(notice),
                .. ResponseDeadline(notice),
                "Abra o pedido no app para aceitar ou recusar. Se não puder receber o pet nessas datas, recusar já ajuda o tutor a procurar outro cantinho.",
            ],
            cancellationToken);
    }

    public Task NotifyPaymentReminderAsync(BookingNotice notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);

        return SendAsync(
            notice,
            PaymentReminderSubject,
            [
                $"Olá, {notice.Name}.",
                $"{notice.OtherPartyName ?? "O anfitrião"} aceitou o seu pedido e está segurando a vaga, mas a 1ª parcela ainda não foi paga.",
                .. Summary(notice),
                .. PaymentDeadline(notice),
                "Pague pelo app, com Pix ou cartão, à vista ou em até 6 parcelas. Se o Pix já foi feito, pode desconsiderar: a confirmação chega em instantes.",
            ],
            cancellationToken);
    }

    public Task NotifyAutoCheckedOutAsync(BookingNotice notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);

        var next = notice.Recipient == BookingParty.Host
            ? "O repasse do que já foi pago foi liberado para a sua conta."
            : "Se o pet ainda não voltou pra casa, fale com o suporte do PetHost.";

        return SendAsync(
            notice,
            AutoCheckedOutSubject,
            [
                $"Olá, {notice.Name}.",
                $"O check-out não foi marcado até {Booking.AutoCheckOutAfterDays} dias depois do dia de saída, então concluímos a estadia.",
                .. Summary(notice),
                next,
            ],
            cancellationToken);
    }

    /// <summary>Cantinho, endereço (exato só quando liberado; senão, o bairro), datas, pets e valor.</summary>
    private static IEnumerable<string> Summary(BookingNotice notice)
    {
        var booking = notice.Booking;
        var nights = booking.Nights == 1 ? "1 noite" : $"{booking.Nights} noites";
        var pets = string.Join(", ", booking.Pets.Select(p => p.Name ?? "pet"));

        yield return $"Cantinho: {booking.Listing?.Title ?? "—"}";

        if (notice.Address is { } address)
            yield return $"Endereço: {Address(address)}";
        else if (booking.Listing is { } listing)
            yield return $"Bairro: {listing.Neighborhood}, {listing.City}/{listing.State}";

        yield return string.Create(PtBr, $"Período: {booking.CheckIn:dd/MM/yyyy} a {booking.CheckOut:dd/MM/yyyy} ({nights})");
        yield return $"Pets: {pets}";
        yield return string.Create(PtBr, $"Valor total: {booking.TotalPrice:C}");
    }

    /// <summary>Até quando responder ao pedido, no horário de Brasília.</summary>
    private static IEnumerable<string> ResponseDeadline(BookingNotice notice)
    {
        if (notice.Booking.ResponseDeadline is { } deadline)
            yield return string.Create(PtBr, $"Responda até {deadline.ToOffset(Brasilia):dd/MM/yyyy 'às' HH:mm} (horário de Brasília). Sem resposta, o pedido expira.");
    }

    /// <summary>Até quando pagar a 1ª parcela, no horário de Brasília.</summary>
    private static IEnumerable<string> PaymentDeadline(BookingNotice notice)
    {
        if (notice.Booking.PaymentDeadline is { } deadline)
            yield return string.Create(PtBr, $"Pague até {deadline.ToOffset(Brasilia):dd/MM/yyyy 'às' HH:mm} (horário de Brasília). Sem o pagamento, o pedido expira e a vaga é liberada.");
    }

    private static IEnumerable<string> Reason(string? reason)
    {
        if (!string.IsNullOrWhiteSpace(reason))
            yield return $"Motivo: {reason}";
    }

    private static IEnumerable<string> Contact(BookingNotice notice, string label)
    {
        if (!string.IsNullOrWhiteSpace(notice.OtherPartyPhone))
            yield return $"{label}: {Phone(notice.OtherPartyPhone)}";
    }

    /// <summary>O reembolso só interessa a quem pagou: o tutor.</summary>
    private static IEnumerable<string> Refund(BookingNotice notice)
    {
        if (notice.Recipient == BookingParty.Owner && notice.Booking.RefundPercentage is { } refund)
            yield return $"Reembolso: {refund}% do valor pago.";
    }

    private static string Address(BookingAddress address)
    {
        var street = string.Join(", ", new[] { address.Street, address.Number, address.Complement }.Where(p => !string.IsNullOrWhiteSpace(p)));
        var zip = address.ZipCode is { Length: 8 } z ? $" — CEP {z[..5]}-{z[5..]}" : string.Empty;

        return $"{street} — {address.Neighborhood}, {address.City}/{address.State}{zip}";
    }

    /// <summary>Gravado só com dígitos: <c>44999990000</c> vira <c>(44) 99999-0000</c>.</summary>
    private static string Phone(string phone) => phone.All(char.IsAsciiDigit)
        ? phone.Length switch
        {
            11 => $"({phone[..2]}) {phone[2..7]}-{phone[7..]}",
            10 => $"({phone[..2]}) {phone[2..6]}-{phone[6..]}",
            _ => phone,
        }
        : phone;

    private async Task SendAsync(BookingNotice notice, string subject, IReadOnlyList<string> paragraphs, CancellationToken cancellationToken)
    {
        var text = string.Join(Environment.NewLine + Environment.NewLine, paragraphs);
        var html = string.Concat(paragraphs.Select(p => $"<p>{WebUtility.HtmlEncode(p)}</p>"));

        await outbox
            .EnqueueAsync(new EmailMessage(notice.Email, notice.Name, subject, text, html,
                new EmailAction("Ver minha estadia", $"/{(notice.Recipient == BookingParty.Owner ? "owner" : "host")}/bookings/{notice.Booking.Id}")), cancellationToken)
            .ConfigureAwait(false);
    }
}
