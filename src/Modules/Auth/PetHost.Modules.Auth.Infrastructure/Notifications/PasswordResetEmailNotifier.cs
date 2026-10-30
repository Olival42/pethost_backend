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
    IOptions<PasswordResetOptions> options,
    IOptions<EmailOptions> emailOptions) : IPasswordResetNotifier
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
            resetUrl is null ? notification.Token : "O link abre a tela de nova senha no site do PetHost.",
            string.Empty,
            $"{(resetUrl is null ? "O código" : "O link")} vale por {minutes} minutos e só pode ser usado uma vez.",
            "Se você não pediu a troca, ignore este e-mail: sua senha continua a mesma.");

        var name = WebUtility.HtmlEncode(notification.FullName);
        var token = WebUtility.HtmlEncode(notification.Token);
        var instruction = resetUrl is null
            ? $"""<p>Use o código na sua integração de recuperação:</p><p style="padding:16px;background-color:#FFF6E6;border:1px solid #E3D6C2;border-radius:12px;font-family:monospace;font-size:16px;word-break:break-all"><strong>{token}</strong></p>"""
            : "<p>Toque em <strong>Criar uma senha nova</strong> para abrir o site do PetHost e escolher sua nova senha. Você não precisa entrar na conta antes.</p>";
        var html = $"""
            <p>Olá, {name}.</p>
            <p>Recebemos um pedido para redefinir a senha da sua conta no PetHost.</p>
            {instruction}
            <p>{(resetUrl is null ? "O código" : "O link")} vale por {minutes} minutos e só pode ser usado uma vez.</p>
            <p>Se você não pediu a troca, ignore este e-mail: sua senha continua a mesma.</p>
            """;

        await outbox
            .EnqueueAsync(
                new EmailMessage(notification.Email, notification.FullName, Subject, text, html,
                    resetUrl is null ? null : new EmailAction("Criar uma senha nova", resetUrl)),
                cancellationToken)
            .ConfigureAwait(false);
    }

    private string? BuildResetUrl(string token)
    {
        if (!string.IsNullOrWhiteSpace(_options.ResetUrl))
            return _options.ResetUrl.Replace(PasswordResetOptions.TokenPlaceholder, Uri.EscapeDataString(token), StringComparison.Ordinal);

        return Uri.TryCreate(emailOptions.Value.AppUrl, UriKind.Absolute, out var app)
            ? new Uri(app, $"/reset-password#token={Uri.EscapeDataString(token)}").AbsoluteUri
            : null;
    }
}
