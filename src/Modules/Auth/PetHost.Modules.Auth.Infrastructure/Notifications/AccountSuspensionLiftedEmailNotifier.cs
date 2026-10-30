using System.Globalization;
using System.Net;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Domain.Users;
using PetHost.Shared.Infrastructure.Email;

namespace PetHost.Modules.Auth.Infrastructure.Notifications;

/// <summary>
/// Monta o aviso de suspensão retirada e deixa na caixa de saída. O envio de verdade
/// acontece em segundo plano (<see cref="IEmailOutbox"/>).
/// </summary>
internal sealed class AccountSuspensionLiftedEmailNotifier(IEmailOutbox outbox) : IAccountSuspensionLiftedNotifier
{
    private const string Subject = "PetHost — sua conta foi liberada";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>O horário vai no fuso de Brasília, que é o do público do MVP.</summary>
    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    public async Task NotifyAsync(AccountSuspensionLiftedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var when = notification.LiftedAt.ToOffset(Brasilia).ToString("dd/MM/yyyy 'às' HH:mm", PtBr);

        // O mesmo e-mail pode ter conta de tutor e de anfitrião: diz qual voltou.
        var account = notification.Role is UserRole.Host ? "conta de anfitrião" : "conta de tutor";

        var next = notification.IsActive
            ? "Você já pode entrar no app normalmente."
            : "A conta continua inativa, como estava antes: para voltar a usá-la, reative pelo app com seu e-mail e senha.";

        var text = string.Join(
            Environment.NewLine,
            $"Olá, {notification.FullName}.",
            string.Empty,
            $"A suspensão da sua {account} no PetHost foi retirada em {when} (horário de Brasília).",
            next,
            string.Empty,
            "Se tiver dúvidas, fale com o suporte.");

        var html = $"""
            <p>Olá, {WebUtility.HtmlEncode(notification.FullName)}.</p>
            <p>A suspensão da sua {account} no PetHost foi retirada em {when} (horário de Brasília).</p>
            <p>{next}</p>
            <p>Se tiver dúvidas, fale com o suporte.</p>
            """;

        await outbox
            .EnqueueAsync(
                new EmailMessage(notification.Email, notification.FullName, Subject, text, html,
                    new EmailAction("Entrar no PetHost", "/login")),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
