using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;
using PetHost.Modules.Payments.Application.Abstractions;
using PetHost.Modules.Payments.Domain.Payments;
using PetHost.Modules.Payments.Infrastructure.Maintenance;
using PetHost.Shared.Infrastructure.Email;

namespace PetHost.Modules.Payments.Infrastructure.Notifications;

/// <summary>
/// E-mails do pagamento: parcela paga, lembrete, atraso (e o lembrete semanal) e reembolso (ao tutor), repasse e estorno
/// (ao anfitrião) e alertas ao admin. Vão pela caixa de saída (<see cref="IEmailOutbox"/>), em segundo plano.
/// </summary>
internal sealed class PaymentEmailNotifier(IEmailOutbox outbox, IOptions<PaymentsOptions> options) : IPaymentNotifier
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public Task NotifyAsync(PaymentNotice notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);

        var amount = Money(notice.AmountCents);
        var installment = notice.InstallmentCount > 1 ? $"a parcela {notice.InstallmentNumber} de {notice.InstallmentCount}" : "o pagamento";
        var due = notice.DueAt is { } dueAt ? string.Create(PtBr, $"{dueAt.ToOffset(PaymentClock.LocalOffset):dd/MM/yyyy}") : "—";

        var (subject, paragraphs) = notice.Kind switch
        {
            PaymentNoticeKind.InstallmentPaid => (
                "PetHost — pagamento recebido",
                new[]
                {
                    $"Recebemos {installment} da sua reserva: {amount}.",
                    "O dinheiro fica guardado com o PetHost e só vai para o anfitrião quando o seu pet voltar pra casa.",
                }),
            PaymentNoticeKind.InstallmentReminder => (
                "PetHost — sua parcela vence em breve",
                new[]
                {
                    $"A parcela {notice.InstallmentNumber} de {notice.InstallmentCount} da sua reserva ({amount}) vence em {due}.",
                    "Pague pelo app, com Pix ou cartão. Se ela vencer antes da chegada do seu pet e não for paga em até 24h, a reserva é cancelada e devolvemos o que você já pagou.",
                    LateFeeTerms,
                }),
            PaymentNoticeKind.InstallmentOverdue => (
                "PetHost — parcela em atraso",
                new[]
                {
                    $"A parcela {notice.InstallmentNumber} de {notice.InstallmentCount} da sua reserva venceu em {due} e não foi paga. Valor para pagar hoje: {amount}.",
                    LateFeeTerms,
                    "Pague pelo app assim que puder. Enquanto houver parcela em atraso, não dá para pedir outra estadia.",
                }),
            PaymentNoticeKind.InstallmentOverdueReminder => (
                "PetHost — parcela ainda em atraso",
                new[]
                {
                    $"A parcela {notice.InstallmentNumber} de {notice.InstallmentCount} da sua reserva, vencida em {due}, continua em aberto. Valor atualizado para pagar hoje: {amount}.",
                    LateFeeTerms,
                    "Pague pelo app, com Pix ou cartão. Enquanto houver parcela em atraso, não dá para pedir outra estadia.",
                }),
            PaymentNoticeKind.RefundCreated => (
                "PetHost — reembolso a caminho",
                new[]
                {
                    $"Estamos devolvendo {amount} referente a {installment} da sua reserva cancelada.",
                    "No cartão, o valor aparece na fatura em alguns dias úteis (depende do banco). No Pix, volta para a conta de onde saiu.",
                }),
            PaymentNoticeKind.PayoutTransferred => (
                "PetHost — repasse enviado",
                new[]
                {
                    $"Enviamos {amount} para a sua conta de recebimento no Stripe, referente a {installment} de uma estadia.",
                    "O Stripe deposita na sua conta bancária no próximo ciclo de pagamento.",
                }),
            PaymentNoticeKind.PayoutReversed => (
                "PetHost — repasse estornado",
                new[]
                {
                    $"O tutor contestou {installment} de uma estadia no cartão e o banco decidiu a favor dele. Por isso, estornamos {amount} do seu repasse.",
                    "Se tiver dúvidas, fale com o suporte do PetHost.",
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(notice), notice.Kind, "Unknown payment notice."),
        };

        return SendAsync(notice.Email, notice.Name, subject, [$"Olá, {notice.Name}.", .. paragraphs], cancellationToken);
    }

    public Task AlertAdminAsync(PaymentAlert alert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var to = options.Value.AdminAlertEmail;
        if (string.IsNullOrWhiteSpace(to))
            return Task.CompletedTask;

        return SendAsync(
            to.Trim(),
            "Admin PetHost",
            $"PetHost — pagamento precisa de atenção ({alert.Issue})",
            [
                $"Pendência: {alert.Issue}",
                $"Reserva: {alert.BookingId}",
                $"Pagamento: {alert.PaymentId} — parcela {alert.InstallmentNumber}",
                $"Valor: {Money(alert.AmountCents)}",
                .. Hint(alert.Issue),
                "Veja os detalhes em GET /api/v1/payments/issues e no Dashboard do Stripe.",
            ],
            cancellationToken);
    }

    /// <summary>O que o admin pode fazer, quando a pendência pede uma decisão dele.</summary>
    private static IEnumerable<string> Hint(string issue)
    {
        if (issue == "installment_overdue_escalated")
            yield return $"A parcela está em atraso há {Payment.OverdueEscalationAfter.Days} dias. O tutor já recebeu o aviso de atraso e os lembretes semanais, que agora param; ele continua sem poder pedir estadia nova até pagar. O valor acima já tem multa e juros. Decida o próximo passo (contato direto, suspensão da conta) — a parcela ainda pode ser paga pelo app.";

        if (issue == "payout_blocked")
            yield return "O Stripe bloqueou os repasses do anfitrião: o repasse ficou retido e sai sozinho quando a conta voltar a receber. Para devolver o dinheiro ao tutor de uma reserva que ainda não começou, cancele como admin (POST /api/v1/bookings/{id}/cancellation, com motivo): o reembolso é de 100%.";
    }

    private static readonly string LateFeeTerms = string.Create(
        PtBr,
        $"Parcela paga depois do vencimento (e das 24h de tolerância) tem multa de {Payment.LateFinePercentage}% e juros de {Payment.MonthlyInterestPercentage}% ao mês, contados por dia.");

    private static string Money(long cents) => string.Create(PtBr, $"{cents / 100m:C}");

    private async Task SendAsync(string email, string name, string subject, IReadOnlyList<string> paragraphs, CancellationToken cancellationToken)
    {
        var text = string.Join(Environment.NewLine + Environment.NewLine, paragraphs);
        var html = string.Concat(paragraphs.Select(p => $"<p>{WebUtility.HtmlEncode(p)}</p>"));

        await outbox
            .EnqueueAsync(new EmailMessage(email, name, subject, text, html), cancellationToken)
            .ConfigureAwait(false);
    }
}
