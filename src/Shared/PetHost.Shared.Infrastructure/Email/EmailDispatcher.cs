using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PetHost.Shared.Infrastructure.Email;

/// <summary>
/// Esvazia a <see cref="EmailOutbox"/> em segundo plano, um e-mail por vez.
/// Falha de envio é registrada e o e-mail é descartado — não derruba o processo
/// nem trava a fila.
/// </summary>
internal sealed partial class EmailDispatcher(
    EmailOutbox outbox,
    IEmailSender sender,
    ILogger<EmailDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in outbox.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            var maskedTo = Mask(message.ToAddress);

            try
            {
                await sender.SendAsync(message, stoppingToken).ConfigureAwait(false);
                LogSent(logger, maskedTo, message.Subject);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Qualquer falha de SMTP vira log; o laço tem de continuar.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogFailed(logger, exception, maskedTo, message.Subject);
            }
        }
    }

    /// <summary>E-mail em log vai mascarado (§15): <c>a***@exemplo.com</c>.</summary>
    private static string Mask(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);

        return at <= 0
            ? "***"
            : string.Concat(email.AsSpan(0, 1), "***", email.AsSpan(at));
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information,
        Message = "Email '{Subject}' sent to {To}.")]
    private static partial void LogSent(ILogger logger, string to, string subject);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Error,
        Message = "Could not send email '{Subject}' to {To}.")]
    private static partial void LogFailed(ILogger logger, Exception exception, string to, string subject);
}
