using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace PetHost.Shared.Infrastructure.Email;

/// <summary>
/// Envio por SMTP com MailKit. O <c>System.Net.Mail.SmtpClient</c> é marcado pela
/// própria Microsoft como não recomendado para código novo e aponta o MailKit.
/// </summary>
internal sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        mime.To.Add(new MailboxAddress(message.ToName ?? string.Empty, message.ToAddress));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder
        {
            TextBody = message.TextBody,
            HtmlBody = message.HtmlBody,
        }.ToMessageBody();

        using var client = new SmtpClient();

        await client
            .ConnectAsync(_options.Host, _options.Port, SecureSocketOptions.Auto, cancellationToken)
            .ConfigureAwait(false);

        if (!string.IsNullOrEmpty(_options.Username))
        {
            await client
                .AuthenticateAsync(_options.Username, _options.Password ?? string.Empty, cancellationToken)
                .ConfigureAwait(false);
        }

        await client.SendAsync(mime, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(quit: true, cancellationToken).ConfigureAwait(false);
    }
}
