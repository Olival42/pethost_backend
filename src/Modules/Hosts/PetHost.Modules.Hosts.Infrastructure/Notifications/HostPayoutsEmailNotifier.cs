using System.Net;
using Microsoft.Extensions.Options;
using PetHost.Modules.Hosts.Application.Abstractions;
using PetHost.Modules.Hosts.Domain.Hosts;
using PetHost.Modules.Hosts.Infrastructure.Payouts;
using PetHost.Shared.Infrastructure.Email;

namespace PetHost.Modules.Hosts.Infrastructure.Notifications;

/// <summary>
/// E-mails do cadastro de recebimento: liberado, lembrete, recusado (ao anfitrião) e o alerta de
/// recusa ao admin. Vão pela caixa de saída (<see cref="IEmailOutbox"/>), em segundo plano.
/// </summary>
internal sealed class HostPayoutsEmailNotifier(IEmailOutbox outbox, IOptions<HostPayoutsOptions> options) : IHostPayoutsNotifier
{
    private const string EnabledSubject = "PetHost — sua conta de recebimento foi aprovada!";
    private const string ReminderSubject = "PetHost — falta pouco para você receber hóspedes";
    private const string RejectedSubject = "PetHost — não foi possível aprovar sua conta de recebimento";
    private const string AdminRejectedSubject = "PetHost — o Stripe recusou a conta de um anfitrião";

    public Task NotifyPayoutsEnabledAsync(HostPayoutsNotice notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);

        return SendAsync(
            notice.Email,
            notice.FullName,
            EnabledSubject,
            [
                $"Olá, {notice.FullName}.",
                "O Stripe aprovou o seu cadastro de recebimento: você já pode receber pelas hospedagens.",
                "Agora é só montar o seu cantinho no app — ele já aparece na busca do PetHost.",
            ],
            cancellationToken);
    }

    public Task NotifyPayoutSetupReminderAsync(HostPayoutsReminder reminder, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reminder);

        var next = reminder.Status switch
        {
            StripeOnboardingStatusValues.NotStarted =>
                "Para receber pelas hospedagens, falta cadastrar a sua conta de recebimento. Abra o app em \"Recebimentos\" e toque em \"Cadastrar\" — leva poucos minutos.",
            StripeOnboardingStatusValues.Restricted =>
                "O Stripe, nosso parceiro de pagamentos, precisa de mais uma informação para liberar os seus recebimentos. Abra o app em \"Recebimentos\" para ver o que falta.",
            _ =>
                "Você começou o cadastro da conta de recebimento, mas ainda não terminou. Abra o app em \"Recebimentos\" e continue de onde parou.",
        };

        return SendAsync(
            reminder.Email,
            reminder.FullName,
            ReminderSubject,
            [
                $"Olá, {reminder.FullName}.",
                next,
                "Enquanto isso, o seu cantinho ainda não pode ser criado nem aparecer na busca. Se precisar de ajuda, responda este e-mail.",
            ],
            cancellationToken);
    }

    public Task NotifyPayoutsRejectedAsync(HostPayoutsNotice notice, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notice);

        return SendAsync(
            notice.Email,
            notice.FullName,
            RejectedSubject,
            [
                $"Olá, {notice.FullName}.",
                "O Stripe, nosso parceiro de pagamentos, não aprovou a sua conta de recebimento.",
                "Por isso ainda não é possível receber pelas hospedagens. Fale com o suporte do PetHost para entender o que aconteceu.",
            ],
            cancellationToken);
    }

    public Task NotifyAdminPayoutsRejectedAsync(HostPayoutsRejection rejection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rejection);

        var adminEmail = options.Value.AdminAlertEmail;
        if (string.IsNullOrWhiteSpace(adminEmail))
            return Task.CompletedTask;

        return SendAsync(
            adminEmail.Trim(),
            "Admin PetHost",
            AdminRejectedSubject,
            [
                $"O Stripe recusou a conta de recebimento do anfitrião {rejection.HostName} (id {rejection.HostId}).",
                $"Motivo informado pelo Stripe: {rejection.Reason ?? "não informado"}.",
                "Confira o anfitrião no painel (GET /api/v1/hosts?stripeStatus=rejected) e decida se ele deve ser suspenso.",
            ],
            cancellationToken);
    }

    private async Task SendAsync(string email, string name, string subject, IReadOnlyList<string> paragraphs, CancellationToken cancellationToken)
    {
        var text = string.Join(Environment.NewLine + Environment.NewLine, paragraphs);
        var html = string.Concat(paragraphs.Select(p => $"<p>{WebUtility.HtmlEncode(p)}</p>"));

        await outbox
            .EnqueueAsync(new EmailMessage(email, name, subject, text, html), cancellationToken)
            .ConfigureAwait(false);
    }
}
