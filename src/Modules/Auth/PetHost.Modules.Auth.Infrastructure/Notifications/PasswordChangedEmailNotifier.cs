using System.Globalization;
using System.Net;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Shared.Infrastructure.Email;

namespace PetHost.Modules.Auth.Infrastructure.Notifications;

/// <summary>
/// Monta o aviso de senha trocada e deixa na caixa de saída. O envio de verdade
/// acontece em segundo plano (<see cref="IEmailOutbox"/>).
/// </summary>
internal sealed class PasswordChangedEmailNotifier(IEmailOutbox outbox) : IPasswordChangedNotifier
{
    private const string Subject = "PetHost — sua senha foi alterada";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>O horário vai no fuso de Brasília, que é o do público do MVP.</summary>
    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    public async Task NotifyAsync(PasswordChangedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var when = notification.ChangedAt.ToOffset(Brasilia).ToString("dd/MM/yyyy 'às' HH:mm", PtBr);

        var text = string.Join(
            Environment.NewLine,
            $"Olá, {notification.FullName}.",
            string.Empty,
            $"A senha da sua conta no PetHost foi alterada em {when} (horário de Brasília).",
            "Por segurança, todas as sessões abertas foram encerradas.",
            string.Empty,
            "Se não foi você, use \"Esqueci minha senha\" no app para criar uma senha nova agora.");

        var html = $"""
            <p>Olá, {WebUtility.HtmlEncode(notification.FullName)}.</p>
            <p>A senha da sua conta no PetHost foi alterada em {when} (horário de Brasília).</p>
            <p>Por segurança, todas as sessões abertas foram encerradas.</p>
            <p><strong>Se não foi você</strong>, use "Esqueci minha senha" no app para criar uma senha nova agora.</p>
            """;

        await outbox
            .EnqueueAsync(
                new EmailMessage(notification.Email, notification.FullName, Subject, text, html),
                cancellationToken)
            .ConfigureAwait(false);
    }
}
