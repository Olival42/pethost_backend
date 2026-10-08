using System.Globalization;
using System.Net;
using Microsoft.Extensions.Options;
using PetHost.Modules.Auth.Application.Abstractions;
using PetHost.Modules.Auth.Infrastructure.Security;
using PetHost.Shared.Infrastructure.Email;

namespace PetHost.Modules.Auth.Infrastructure.Notifications;

/// <summary>
/// Monta o e-mail de troca de senha e deixa na caixa de saída. O envio de verdade
/// acontece em segundo plano (<see cref="IEmailOutbox"/>).
/// </summary>
internal sealed class PasswordResetEmailNotifier(
    IEmailOutbox outbox,
    IOptions<PasswordResetOptions> options) : IPasswordResetNotifier
{
    private const string Subject = "PetHost — redefinição de senha";

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private readonly PasswordResetOptions _options = options.Value;

    public async Task NotifyAsync(PasswordResetNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var minutes = _options.TokenLifetimeMinutes.ToString(PtBr);
        var resetUrl = BuildResetUrl(notification.Token);

        var text = string.Join(
            Environment.NewLine,
            $"Olá, {notification.FullName}.",
            string.Empty,
            "Recebemos um pedido para redefinir a senha da sua conta no PetHost.",
            resetUrl is null
                ? "Use o código abaixo na tela de nova senha:"
                : $"Para criar uma senha nova, acesse: {resetUrl}",
            string.Empty,
            resetUrl is null ? notification.Token : $"Ou use este código: {notification.Token}",
            string.Empty,
            $"O código vale por {minutes} minutos e só pode ser usado uma vez.",
            "Se você não pediu a troca, ignore este e-mail: sua senha continua a mesma.");

        var name = WebUtility.HtmlEncode(notification.FullName);
        var token = WebUtility.HtmlEncode(notification.Token);
        var link = resetUrl is null
            ? string.Empty
            : $"""<p><a href="{WebUtility.HtmlEncode(resetUrl)}">Criar uma senha nova</a></p><p>Ou use este código:</p>""";

        var html = $"""
            <p>Olá, {name}.</p>
            <p>Recebemos um pedido para redefinir a senha da sua conta no PetHost.</p>
            {(resetUrl is null ? "<p>Use o código abaixo na tela de nova senha:</p>" : link)}
            <p style="font-family:monospace;font-size:16px;word-break:break-all"><strong>{token}</strong></p>
            <p>O código vale por {minutes} minutos e só pode ser usado uma vez.</p>
            <p>Se você não pediu a troca, ignore este e-mail: sua senha continua a mesma.</p>
            """;

        await outbox
            .EnqueueAsync(
                new EmailMessage(notification.Email, notification.FullName, Subject, text, html),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private string? BuildResetUrl(string token) =>
        string.IsNullOrWhiteSpace(_options.ResetUrl)
            ? null
            : _options.ResetUrl.Replace(
                PasswordResetOptions.TokenPlaceholder,
                Uri.EscapeDataString(token),
                StringComparison.Ordinal);
}
